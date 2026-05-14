using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PS2Desktop.Vistas
{
    public partial class DescargasView : UserControl
    {
        private readonly IDownloadRepository _downloadRepo;
        private readonly MediaFireService _mediaFire;
        private List<DownloadItem> _items = new();

        private static Guid? _pendingItemId;
        private static string _pendingSavePath;
        private DateTime _downloadStartTime;
        private long _lastBytes;

        private static ConcurrentDictionary<Guid, DownloadProgress> _activeDownloads = new();

        private class DownloadProgress
        {
            public long BytesRead { get; set; }
            public long TotalBytes { get; set; }
            public double Speed { get; set; }
            public double MaxSpeed { get; set; }
            public double Progress { get; set; }
            public List<double> WaveformHistory { get; set; } = new();
            public DateTime LastUpdate { get; set; }
            public bool Completed { get; set; }
            public bool Canceled { get; set; }
            public string SavePath { get; set; }
        }

        public static void SetPendingDownload(Guid itemId, string savePath)
        {
            _pendingItemId = itemId;
            _pendingSavePath = savePath;
        }

        public DescargasView()
        {
            InitializeComponent();
            _downloadRepo = App.ServiceProvider.GetRequiredService<IDownloadRepository>();
            _mediaFire = App.ServiceProvider.GetRequiredService<MediaFireService>();
        }

        public async void ProcesarPendientes()
        {
            try
            {
                if (_pendingItemId.HasValue && !string.IsNullOrEmpty(_pendingSavePath))
                {
                    var id = _pendingItemId.Value;
                    var path = _pendingSavePath;
                    _pendingItemId = null;
                    _pendingSavePath = null;
                    await CargarDescargas();
                    await IniciarDescarga(id, path);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al procesar descarga: " + ex.Message, "Error");
            }
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await CargarDescargas();

            // Re-attach UI updates for any downloads still in progress
            foreach (var kv in _activeDownloads)
            {
                if (!kv.Value.Completed)
                    _ = Task.Run(async () =>
                    {
                        while (!kv.Value.Completed)
                        {
                            await Task.Delay(200);
                            try
                            {
                                await Dispatcher.InvokeAsync(() =>
                                    ActualizarProgreso(kv.Key, kv.Value.Progress,
                                        kv.Value.BytesRead, kv.Value.TotalBytes, kv.Value.Speed));
                            }
                            catch { break; }
                        }
                    });
            }

            ProcesarPendientes();
        }

        private async Task IniciarDescarga(Guid itemId, string savePath)
        {
            var item = _items.FirstOrDefault(i => i.Id == itemId);
            if (item == null || string.IsNullOrEmpty(item.DirectUrl))
            {
                System.Diagnostics.Debug.WriteLine($"IniciarDescarga: item=null={item==null}, DirectUrl empty={string.IsNullOrEmpty(item?.DirectUrl)}");
                return;
            }

            item.Status = "downloading";
            await _downloadRepo.UpdateAsync(item);
            RenderizarLista();

            _downloadStartTime = DateTime.UtcNow;
            _lastBytes = 0;

            long existingBytes = 0;
            bool fileExists = File.Exists(savePath);
            if (fileExists)
            {
                var fileInfo = new FileInfo(savePath);
                existingBytes = fileInfo.Length;
                _lastBytes = existingBytes;
            }

            var state = new DownloadProgress { TotalBytes = -1, SavePath = savePath, BytesRead = existingBytes };
            _activeDownloads[itemId] = state;

            var wasCanceled = false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };

                HttpResponseMessage response;
                if (existingBytes > 0)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, item.DirectUrl);
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
                    response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                }
                else
                {
                    response = await http.GetAsync(item.DirectUrl, HttpCompletionOption.ResponseHeadersRead);
                }
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                if (existingBytes > 0 && totalBytes > 0)
                    totalBytes += existingBytes;
                state.TotalBytes = totalBytes;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = fileExists 
                    ? new FileStream(savePath, FileMode.Append, FileAccess.Write, FileShare.Read)
                    : File.Create(savePath);

                var buffer = new byte[81920];
                long bytesRead = 0;
                int read;
                var lastUpdate = DateTime.MinValue;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (state.Canceled) { wasCanceled = true; break; }
                    await fileStream.WriteAsync(buffer, 0, read);
                    bytesRead += read;
                    state.BytesRead = bytesRead;

                    if (DateTime.UtcNow - lastUpdate > TimeSpan.FromMilliseconds(200))
                    {
                        lastUpdate = DateTime.UtcNow;
                        var progress = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                        var elapsed = (DateTime.UtcNow - _downloadStartTime).TotalSeconds;
                        var speed = elapsed > 0 ? bytesRead / elapsed : 0;
                        state.Progress = progress;
                        state.Speed = speed;
                        if (speed > state.MaxSpeed) state.MaxSpeed = speed;
                        state.WaveformHistory.Add(speed);
                        if (state.WaveformHistory.Count > 30) state.WaveformHistory.RemoveAt(0);
                        state.LastUpdate = DateTime.UtcNow;
                        ActualizarProgreso(item.Id, progress, bytesRead, totalBytes, speed);
                        if (state.Canceled) { wasCanceled = true; break; }
                    }
                }
            }
            catch (Exception ex)
            {
                state.Completed = true;
                item.Status = "error";
                SafeDelete(savePath);
                MessageBox.Show("Error al descargar: " + ex.Message, "Error");
            }

            if (wasCanceled)
            {
                state.Completed = true;
                item.Status = "paused";
                item.SavePath = savePath;
            }
            else if (!state.Completed)
            {
                state.Completed = true;
                item.Status = "completed";
                SoundService.PlayDownloadComplete();
                MessageBox.Show(
                    $"Descarga completada.\nGuardado en: {savePath}\n\nContraseña RAR: gamesgx.net",
                    "Descarga completada");
            }

            await _downloadRepo.UpdateAsync(item);
            RenderizarLista();

            if (item.Status != "paused")
                _activeDownloads.TryRemove(itemId, out _);
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private async Task CargarDescargas()
        {
            try
            {
                _items = await _downloadRepo.GetAllAsync();
                RenderizarLista();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar descargas: " + ex.Message, "Error");
            }
        }

        private void RenderizarLista()
        {
            DownloadsPanel.Children.Clear();

            if (_items.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;

            foreach (var item in _items)
                DownloadsPanel.Children.Add(CrearTarjeta(item));
        }

        private Border CrearTarjeta(DownloadItem item)
        {
            var isDownloading = item.Status == "downloading";

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x1E, 0x1E)),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 16),
                Padding = new Thickness(24, 18, 20, 18),
                Tag = item,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xCC, 0xCC, 0xCC)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    Opacity = 0.2,
                    Color = Colors.Black
                }
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // --- Left column: game info ---
            var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // Name row with image
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };

            if (!string.IsNullOrEmpty(item.ImageUrl))
            {
                var img = new Image
                {
                    Width = 48,
                    Height = 48,
                    Margin = new Thickness(0, 0, 12, 0),
                    Stretch = Stretch.UniformToFill,
                    VerticalAlignment = VerticalAlignment.Center
                };
                try
                {
                    img.Source = new BitmapImage(new Uri(item.ImageUrl, UriKind.Absolute));
                }
                catch { }
                nameRow.Children.Add(img);
            }

            nameRow.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(item.FileName) ? item.Url : item.FileName,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            leftStack.Children.Add(nameRow);

            // Status text
            var statusColor = isDownloading
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x99, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            var statusText = item.Status switch
            {
                "downloading" => "Descargando…",
                "paused" => "Pausado",
                "pending" => "Pendiente",
                "resolving" => "Resolviendo…",
                "ready" => "Listo",
                "completed" => "Completado",
                "error" => "Error",
                _ => item.Status
            };
            leftStack.Children.Add(new TextBlock
            {
                Text = statusText,
                Foreground = statusColor,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0)
            });

            // Stats row (Descarga · Lectura · Escritura · Max) — visible only when downloading
            if (isDownloading)
            {
                var statsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                statsPanel.Children.Add(CrearStatLabel("Descarga", "0 Mbps", Colors.White));
                statsPanel.Children.Add(CrearStatLabel("Lectura", "0 Mbps", new Color { R = 0x88, G = 0x88, B = 0x88, A = 0xFF }));
                statsPanel.Children.Add(CrearStatLabel("Escritura", "0 Mbps", Color.FromRgb(0xFF, 0x14, 0x93)));
                statsPanel.Children.Add(CrearStatLabel("Max", "0 Mbps", Color.FromRgb(0x00, 0xE6, 0x76)));

                var writePanel = new StackPanel { Orientation = Orientation.Horizontal };
                writePanel.Children.Add(statsPanel);

                // Waveform graph — updates dynamically
                var waveformCanvas = new Canvas
                {
                    Width = 100,
                    Height = 28,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = item.Id + ":WaveformCanvas"
                };
                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x14, 0x93)),
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round,
                    Tag = item.Id + ":WaveformLine"
                };
                // Start with flat line
                var pts = new PointCollection();
                for (int i = 0; i < 30; i++)
                    pts.Add(new Point(i * 3.33, 14));
                polyline.Points = pts;
                waveformCanvas.Children.Add(polyline);
                writePanel.Children.Add(waveformCanvas);

                leftStack.Children.Add(writePanel);
            }

            // Progress bar
            var progressTrack = new Border
            {
                Height = 5,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 12, 80, 0),
                Tag = "ProgressBar"
            };
            var progressFill = new Border
            {
                Width = 0, Height = 5,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = "ProgressFill"
            };
            if (isDownloading)
            {
                progressFill.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0x00, 0x99, 0xFF), 0),
                        new GradientStop(Color.FromRgb(0x99, 0x33, 0xFF), 1)
                    }
                };
            }
            else
            {
                progressFill.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            }
            progressTrack.Child = progressFill;
            leftStack.Children.Add(progressTrack);

            // Progress text (MB/GB out of total)
            var progressText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 12,
                Margin = new Thickness(0, 6, 80, 0),
                Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed,
                Tag = "ProgressText"
            };
            leftStack.Children.Add(progressText);

            Grid.SetColumn(leftStack, 0);
            mainGrid.Children.Add(leftStack);

            // --- Right column: action buttons ---
            var btnPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };

            // Round Pause/Play button
            var pauseBtn = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = item.Id,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var pauseIcon = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (isDownloading)
            {
                // Two vertical bars pause icon
                pauseIcon.Children.Add(new Border
                {
                    Width = 3, Height = 12,
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(1),
                    Margin = new Thickness(0, 0, 3, 0)
                });
                pauseIcon.Children.Add(new Border
                {
                    Width = 3, Height = 12,
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(1)
                });
                pauseBtn.Child = pauseIcon;
                pauseBtn.MouseDown += (s, e) =>
                {
                    if (s is Border b && b.Tag is Guid id)
                    {
                        if (_activeDownloads.TryGetValue(id, out var ds))
                            ds.Canceled = true;
                    }
                };
            }
            else if (item.Status == "paused")
            {
                // Play icon (triangle)
                var playIcon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M0,0 L0,14 L12,7 Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    Width = 12,
                    Height = 14,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                pauseIcon.Children.Add(playIcon);
                pauseBtn.Child = pauseIcon;
                pauseBtn.MouseDown += (s, e) =>
                {
                    if (s is Border b && b.Tag is Guid id)
                    {
                        var it = _items.FirstOrDefault(i => i.Id == id);
                        Debug.WriteLine("[RESUME] id=" + id + " item=" + (it != null) + " status=" + (it?.Status ?? "null") + " savePath=" + (it?.SavePath ?? "null"));
                        if (it != null && !string.IsNullOrEmpty(it.SavePath))
                        {
                            _ = IniciarDescarga(id, it.SavePath);
                        }
                    }
                };
            }
            else
            {
                // Fallback: simple disabled icon
                pauseIcon.Children.Add(new Border
                {
                    Width = 3, Height = 12,
                    Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    CornerRadius = new CornerRadius(1),
                    Margin = new Thickness(0, 0, 3, 0)
                });
                pauseIcon.Children.Add(new Border
                {
                    Width = 3, Height = 12,
                    Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    CornerRadius = new CornerRadius(1)
                });
                pauseBtn.Child = pauseIcon;
            }
            btnPanel.Children.Add(pauseBtn);

            // Round X button
            var xBtn = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = item.Id
            };
            var xIcon = new TextBlock
            {
                Text = "✕",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            xBtn.Child = xIcon;
            xBtn.MouseDown += (s, e) => BtnEliminar_Click(s, e);
            btnPanel.Children.Add(xBtn);

            Grid.SetColumn(btnPanel, 1);
            mainGrid.Children.Add(btnPanel);

            border.Child = mainGrid;
            return border;
        }

        private StackPanel CrearStatLabel(string label, string value, Color color)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 18, 0)
            };
            sp.Children.Add(new TextBlock
            {
                Text = label + " ",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 12
            });
            sp.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(color),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Tag = label + "Value"
            });
            return sp;
        }

        private async Task IniciarDescargaConDialogo(DownloadItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.DirectUrl)) return;

            var saveDialog = new SaveFileDialog
            {
                FileName = item.FileName ?? "download",
                Filter = "Todos los archivos|*.*"
            };

            if (saveDialog.ShowDialog() != true) return;

            var savePath = saveDialog.FileName;

            item.Status = "downloading";
            await _downloadRepo.UpdateAsync(item);
            RenderizarLista();
            _downloadStartTime = DateTime.UtcNow;
            _lastBytes = 0;

            var state = new DownloadProgress { TotalBytes = -1, SavePath = savePath };
            _activeDownloads[item.Id] = state;
            var wasCanceled = false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
                using var response = await http.GetAsync(item.DirectUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                state.TotalBytes = totalBytes;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(savePath);

                var buffer = new byte[81920];
                long bytesRead = 0;
                int read;
                var lastUpdate = DateTime.MinValue;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (state.Canceled) { wasCanceled = true; break; }
                    await fileStream.WriteAsync(buffer, 0, read);
                    bytesRead += read;
                    state.BytesRead = bytesRead;

                    if (DateTime.UtcNow - lastUpdate > TimeSpan.FromMilliseconds(200))
                    {
                        lastUpdate = DateTime.UtcNow;
                        var progress = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                        var elapsed = (DateTime.UtcNow - _downloadStartTime).TotalSeconds;
                        var speed = elapsed > 0 ? bytesRead / elapsed : 0;
                        state.Progress = progress;
                        state.Speed = speed;
                        if (speed > state.MaxSpeed) state.MaxSpeed = speed;
                        state.WaveformHistory.Add(speed);
                        if (state.WaveformHistory.Count > 30) state.WaveformHistory.RemoveAt(0);
                        state.LastUpdate = DateTime.UtcNow;
                        ActualizarProgreso(item.Id, progress, bytesRead, totalBytes, speed);
                        if (state.Canceled) { wasCanceled = true; break; }
                    }
                }
            }
            catch (Exception ex)
            {
                state.Completed = true;
                item.Status = "error";
                SafeDelete(savePath);
                MessageBox.Show("Error al descargar: " + ex.Message, "Error");
            }

            if (wasCanceled)
            {
                state.Completed = true;
                item.Status = "paused";
                item.SavePath = savePath;
            }
            else if (!state.Completed)
            {
                state.Completed = true;
                item.Status = "completed";
                SoundService.PlayDownloadComplete();
                MessageBox.Show(
                    $"Descarga completada.\nGuardado en: {savePath}\n\nContraseña RAR: gamesgx.net",
                    "Descarga completada");
            }

            await _downloadRepo.UpdateAsync(item);
            RenderizarLista();

            if (item.Status != "paused")
                _activeDownloads.TryRemove(item.Id, out _);
        }

        private string FormatearInfo(DownloadItem item)
        {
            var parts = new List<string>();
            if (item.FileSize.HasValue)
                parts.Add(FormatearTamaño(item.FileSize.Value));
            parts.Add(item.Status switch
            {
                "pending" => "Pendiente",
                "resolving" => "Resolviendo...",
                "ready" => "Listo para descargar",
                "downloading" => "Descargando...",
                "paused" => "Pausado",
                "completed" => "Completado",
                "error" => "Error",
                _ => item.Status
            });
            parts.Add($"{item.CreatedAt:MMM dd, yyyy}");
            return string.Join(" · ", parts);
        }

        private static string FormatearTamaño(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };

        private static string ObtenerStatusText(string status) => status switch
        {
            "pending" => "Pendiente",
            "resolving" => "Resolviendo",
            "ready" => "Listo",
            "downloading" => "Descargando",
            "paused" => "Pausado",
            "completed" => "Completado",
            "error" => "Error",
            _ => status
        };

        private static SolidColorBrush ObtenerStatusColor(string status) => status switch
        {
            "pending" => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
            "resolving" => new SolidColorBrush(Color.FromRgb(0x00, 0x99, 0xFF)),
            "ready" => new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
            "downloading" => new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0xAA)),
            "paused" => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            "completed" => new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
            "error" => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x45)),
            _ => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
        };

        private async void BtnResolver_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Guid id) return;

            btn.IsEnabled = false;
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;

            try
            {
                item.Status = "resolving";
                await _downloadRepo.UpdateAsync(item);
                RenderizarLista();

                var (directUrl, fileName, fileSize) = await _mediaFire.ResolveAsync(item.Url);

                if (string.IsNullOrEmpty(directUrl))
                {
                    item.Status = "error";
                    MessageBox.Show("No se pudo resolver el enlace directo.\nVerifica que la URL sea válida.", "Error");
                }
                else
                {
                    item.DirectUrl = directUrl;
                    item.FileName = fileName;
                    item.FileSize = fileSize;
                    item.Status = "ready";
                }
            }
            catch (Exception ex)
            {
                item.Status = "error";
                MessageBox.Show("Error al resolver: " + ex.Message, "Error");
            }

            await _downloadRepo.UpdateAsync(item);
            RenderizarLista();
        }

        private async void BtnDescargar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Guid id) return;

            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item == null || string.IsNullOrEmpty(item.DirectUrl)) return;

            var saveDialog = new SaveFileDialog
            {
                FileName = item.FileName ?? "download",
                Filter = "Todos los archivos|*.*"
            };

            if (saveDialog.ShowDialog() != true) return;

            var savePath = saveDialog.FileName;

            btn.IsEnabled = false;
            item.Status = "downloading";
            await _downloadRepo.UpdateAsync(item);
            RenderizarLista();
            _downloadStartTime = DateTime.UtcNow;

            var state = new DownloadProgress { TotalBytes = -1, SavePath = savePath };
            _activeDownloads[item.Id] = state;
            var wasCanceled = false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
                using var response = await http.GetAsync(item.DirectUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                state.TotalBytes = totalBytes;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(savePath);

                var buffer = new byte[81920];
                long bytesRead = 0;
                int read;
                var lastUpdate = DateTime.MinValue;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (state.Canceled) { wasCanceled = true; break; }
                    await fileStream.WriteAsync(buffer, 0, read);
                    bytesRead += read;
                    state.BytesRead = bytesRead;

                    if (DateTime.UtcNow - lastUpdate > TimeSpan.FromMilliseconds(200))
                    {
                        lastUpdate = DateTime.UtcNow;
                        var progress = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                        var elapsed = (DateTime.UtcNow - _downloadStartTime).TotalSeconds;
                        var speed = elapsed > 0 ? bytesRead / elapsed : 0;
                        state.Progress = progress;
                        state.Speed = speed;
                        if (speed > state.MaxSpeed) state.MaxSpeed = speed;
                        state.WaveformHistory.Add(speed);
                        if (state.WaveformHistory.Count > 30) state.WaveformHistory.RemoveAt(0);
                        state.LastUpdate = DateTime.UtcNow;
                        ActualizarProgreso(item.Id, progress, bytesRead, totalBytes, speed);
                        if (state.Canceled) { wasCanceled = true; break; }
                    }
                }
            }
            catch (Exception ex)
            {
                state.Completed = true;
                item.Status = "error";
                SafeDelete(savePath);
                MessageBox.Show("Error al descargar: " + ex.Message, "Error");
            }

            if (wasCanceled)
            {
                state.Completed = true;
                item.Status = "paused";
                item.SavePath = savePath;
            }
            else if (!state.Completed)
            {
                state.Completed = true;
                item.Status = "completed";
                SoundService.PlayDownloadComplete();
                MessageBox.Show(
                    $"Descarga completada.\nGuardado en: {savePath}\n\nContraseña RAR: gamesgx.net",
                    "Descarga completada");
            }

            await _downloadRepo.UpdateAsync(item);
            RenderizarLista();

            if (item.Status != "paused")
                _activeDownloads.TryRemove(item.Id, out _);
        }

        private void ActualizarProgreso(Guid itemId, double progress, long bytesRead = 0, long totalBytes = 0, double speed = 0)
        {
            _activeDownloads.TryGetValue(itemId, out var state);
            var maxSpeed = state?.MaxSpeed ?? speed;

            foreach (Border border in DownloadsPanel.Children)
            {
                if (border.Tag is DownloadItem di && di.Id == itemId && border.Child is Grid mainGrid)
                {
                    if (mainGrid.Children.Count < 2) continue;
                    var leftStack = mainGrid.Children[0] as StackPanel;
                    if (leftStack == null) continue;

                    foreach (var child in leftStack.Children)
                    {
                        // Update stats + waveform inside writePanel
                        if (child is StackPanel writePanel)
                        {
                            var canvasTag = itemId + ":WaveformCanvas";
                            var lineTag = itemId + ":WaveformLine";

                            foreach (var inner in writePanel.Children)
                            {
                                // Update stats
                                if (inner is StackPanel statsRow)
                                {
                                    foreach (var statLabel in statsRow.Children)
                                    {
                                        if (statLabel is StackPanel labelRow)
                                        {
                                            foreach (var tb in labelRow.Children)
                                            {
                                                if (tb is TextBlock t && t.Tag is string tag)
                                                {
                                                    var speedMbps = speed / 1024.0 / 1024.0 * 8;
                                                    var maxMbps = maxSpeed / 1024.0 / 1024.0 * 8;
                                                    t.Text = tag switch
                                                    {
                                                        "DescargaValue" => $"{speedMbps:F2} Mbps",
                                                        "LecturaValue" => "0 Mbps",
                                                        "EscrituraValue" => $"{speedMbps * 0.9:F2} Mbps",
                                                        "MaxValue" => $"{maxMbps:F2} Mbps",
                                                        _ => t.Text
                                                    };
                                                }
                                            }
                                        }
                                    }
                                }

                                // Update waveform
                                if (inner is Canvas canvas && canvas.Tag as string == canvasTag)
                                {
                                    foreach (var childElem in canvas.Children)
                                    {
                                        if (childElem is Polyline pl && pl.Tag as string == lineTag)
                                        {
                                            var history = state?.WaveformHistory ?? new List<double>();
                                            var maxHist = history.Count > 0 ? history.Max() : 1;
                                            if (maxHist < 1) maxHist = 1;
                                            var pts = new PointCollection();
                                            for (int i = 0; i < history.Count; i++)
                                            {
                                                double x = i * (100.0 / Math.Max(history.Count - 1, 1));
                                                double normalized = Math.Min(history[i] / maxHist, 1.0);
                                                double y = 26 - normalized * 24;
                                                pts.Add(new Point(x, y));
                                            }
                                            pl.Points = pts;
                                        }
                                    }
                                }
                            }
                        }

                        // Update progress bar
                        if (child is Border pb && pb.Tag as string == "ProgressBar" && pb.Child is Border fill)
                        {
                            fill.Width = pb.ActualWidth * progress / 100;
                            if (fill.Width < 0) fill.Width = 0;
                        }

                        // Update progress text (bytes out of total)
                        if (child is TextBlock pt && pt.Tag as string == "ProgressText")
                        {
                            if (totalBytes > 0)
                                pt.Text = $"{FormatearTamaño(bytesRead)} / {FormatearTamaño(totalBytes)}";
                            else
                                pt.Text = FormatearTamaño(bytesRead);
                        }
                    }
                }
            }
        }

        private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            Guid id = default;
            if (sender is Border b && b.Tag is Guid g)
                id = g;
            else if (sender is Button btn && btn.Tag is Guid g2)
                id = g2;
            else
                return;

            var result = MessageBox.Show("¿Eliminar esta descarga?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _downloadRepo.DeleteAsync(id);
                await CargarDescargas();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error");
            }
        }
    }
}
