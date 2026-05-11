using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace PS2Desktop.Vistas
{
    public partial class DetalleJuegosView : UserControl, IDisposable
    {
        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private bool _isDraggingSlider;
        private readonly YoutubeClient _youtubeClient = new();
        private readonly IVoteRepository _voteRepo;
        private readonly ISessionService _session;
        private readonly MediaFireService _mediaFire;
        private readonly IDownloadRepository _downloadRepo;
        private Game _game;
        private int _userVote;

        private const string DefaultVideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        private const string ImageBase = "https://raw.githubusercontent.com/Luden02/psx-ps2-opl-art-database/main/PS2";

        private List<string> _mediaSources = new();
        private int _currentIndex;

        public DetalleJuegosView()
        {
            Core.Initialize();
            InitializeComponent();

            _voteRepo = App.ServiceProvider.GetRequiredService<IVoteRepository>();
            _session = App.ServiceProvider.GetRequiredService<ISessionService>();
            _mediaFire = App.ServiceProvider.GetRequiredService<MediaFireService>();
            _downloadRepo = App.ServiceProvider.GetRequiredService<IDownloadRepository>();

            _libVLC = new LibVLC();
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
            VideoPlayer.MediaPlayer = _mediaPlayer;

            _mediaPlayer.TimeChanged += (s, e) =>
            {
                if (!_isDraggingSlider)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_mediaPlayer == null || TimelineSlider == null) return;
                        TimelineSlider.Value = _mediaPlayer.Position * 100;
                        var t = TimeSpan.FromMilliseconds(e.Time);
                        lblTime.Text = $"{t.Minutes}:{t.Seconds:D2}";
                        if (_mediaPlayer.Length > 0)
                        {
                            var total = TimeSpan.FromMilliseconds(_mediaPlayer.Length);
                            lblTotalTime.Text = $"{total.Minutes}:{total.Seconds:D2}";
                        }
                    }));
                }
            };

            this.Loaded += async (s, e) =>
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                BeginAnimation(OpacityProperty, fadeIn);

                MainScrollViewer.Focus();
                await Task.Delay(300);
                await CambiarMedia(0);
            };
            this.Unloaded += (s, e) => Dispose();
        }

        public void SetGame(Game game)
        {
            _game = game;
            this.DataContext = game;

            lblTitle.Text = game.nombre ?? "Sin título";
            lblDescripcion.Text = game.descripcion ?? "Sin descripción";

            lblDeveloper.Text = string.IsNullOrEmpty(game.autor) ? "" : game.autor;
            lblPublisher.Text = string.IsNullOrEmpty(game.publisher) ? "" : $"• {game.publisher}";
            lblPublisher.Visibility = string.IsNullOrEmpty(game.publisher)
                ? Visibility.Collapsed : Visibility.Visible;

            lblGameId.Text = game.game_id;
            lblGameId.Visibility = string.IsNullOrEmpty(game.game_id)
                ? Visibility.Collapsed : Visibility.Visible;

            lblRegion.Text = game.region;
            lblRegion.Visibility = string.IsNullOrEmpty(game.region)
                ? Visibility.Collapsed : Visibility.Visible;

            var mt = game.media_type;
            lblMediaType.Text = mt;
            lblMediaType.Visibility = string.IsNullOrEmpty(mt)
                ? Visibility.Collapsed : Visibility.Visible;

            // Full date in title area
            lblYear.Text = game.fecha_lanzamiento;
            lblYear.Visibility = string.IsNullOrEmpty(game.fecha_lanzamiento)
                ? Visibility.Collapsed : Visibility.Visible;

            // Sidebar details
            lblGenero.Text = game.genero ?? "";
            lblDevDetail.Text = game.autor ?? "";
            lblPubDetail.Text = game.publisher ?? "";
            lblFechaDetail.Text = game.fecha_lanzamiento ?? "";
            lblJugadores.Text = game.jugadores ?? "";
            lblResolucion.Text = game.resolucion ?? "";
            lblWidescreen.Text = game.widescreen ? "Sí" : "No";

            if (game.caracteristicas != null && game.caracteristicas.Count > 0)
            {
                CaracteristicasList.ItemsSource = game.caracteristicas;
                CaracteristicasList.Visibility = Visibility.Visible;
            }

            // Poster
            if (!string.IsNullOrEmpty(game.image_url))
                SetImageSafe(PosterImage, game.image_url);

            // Sidebar
            if (!string.IsNullOrEmpty(game.link_descarga))
            {
                btnDownload.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "⬇", FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "DESCARGAR", FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
                btnDownload.IsEnabled = true;
            }
            else
            {
                btnDownload.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "✕", FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "No disponible", FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
                btnDownload.IsEnabled = false;
            }

            // Rating
            _ = CargarRatingAsync(game.id);
            VotePanel.Visibility = Visibility.Visible;
            ConfigurarEstrellas();

            // Build media sources: trailer + background images
            _mediaSources = new List<string>();
            if (!string.IsNullOrEmpty(game.video_demo))
                _mediaSources.Add(game.video_demo);
            else
                _mediaSources.Add(null);

            // Try background images dynamically
            var gid = game.game_id;
            if (!string.IsNullOrEmpty(gid))
            {
                for (int i = 0; i < 4; i++)
                    _mediaSources.Add($"{ImageBase}/{gid}/{gid}_BG_{i:D2}.png");
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    _mediaSources.Add(null);
            }

            _currentIndex = 0;
            LoadThumbnails();
        }

        private void LoadThumbnails()
        {
            var thumbs = new[] { ThumbImg0, ThumbImg1, ThumbImg2, ThumbImg3 };
            for (int i = 0; i < thumbs.Length; i++)
            {
                int idx = i + 1;
                if (idx < _mediaSources.Count && _mediaSources[idx] != null)
                    SetImageSafe(thumbs[i], _mediaSources[idx]);
            }
        }

        private static void SetImageSafe(Image img, string url)
        {
            try
            {
                img.Source = new BitmapImage(new Uri(url, UriKind.Absolute));
            }
            catch { }
        }

        private async Task CargarRatingAsync(Guid gameId)
        {
            try
            {
                var (avg, cnt) = await _voteRepo.GetAverageRatingAsync(gameId, "game");
                if (cnt > 0)
                {
                    RatingPanel.Visibility = Visibility.Visible;
                    int fullStars = (int)Math.Round(avg, MidpointRounding.AwayFromZero);
                    string stars = new string('★', fullStars).PadRight(5, '☆');
                    lblStars.Text = stars;
                    lblRatingValue.Text = avg.ToString("F1");
                    lblRatingCount.Text = $"({cnt} {(cnt == 1 ? "voto" : "votos")})";
                }
            }
            catch { }
        }

        // --- Media navigation ---

        private async Task CambiarMedia(int index)
        {
            _currentIndex = index;
            LoadingPanel.Visibility = Visibility.Visible;
            ActualizarBordesMiniaturas(index);

            try
            {
                if (index >= _mediaSources.Count || _mediaSources[index] == null)
                {
                    VideoPlayer.Visibility = Visibility.Collapsed;
                    CoverImage.Visibility = Visibility.Collapsed;
                    MostrarControles(false);
                    return;
                }

                bool esVideo = index == 0;
                MostrarControles(esVideo);

                if (esVideo)
                {
                    CoverImage.Visibility = Visibility.Collapsed;
                    VideoPlayer.Visibility = Visibility.Visible;
                    await ReproducirVideo(_mediaSources[index]);
                }
                else
                {
                    _mediaPlayer.Stop();
                    VideoPlayer.Visibility = Visibility.Collapsed;
                    CoverImage.Visibility = Visibility.Visible;
                    SetImageSafe(CoverImage, _mediaSources[index]);
                    await Task.Delay(300);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error al cambiar media: " + ex.Message);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task ReproducirVideo(string videoUrl)
        {
            try
            {
                var manifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoUrl);
                var streamInfo = manifest.GetMuxedStreams().GetWithHighestVideoQuality();

                if (streamInfo != null)
                {
                    using var media = new Media(_libVLC, new Uri(streamInfo.Url));
                    _mediaPlayer.Play(media);
                    btnPlayPause.Content = FindResource("IconPause");
                    return;
                }
            }
            catch { Debug.WriteLine("Fallo video, intentando fallback"); }

            if (videoUrl != DefaultVideoUrl)
            {
                try
                {
                    var fb = await _youtubeClient.Videos.Streams.GetManifestAsync(DefaultVideoUrl);
                    var fbStream = fb.GetMuxedStreams().GetWithHighestVideoQuality();
                    if (fbStream != null)
                    {
                        _mediaSources[0] = DefaultVideoUrl;
                        using var media = new Media(_libVLC, new Uri(fbStream.Url));
                        _mediaPlayer.Play(media);
                        btnPlayPause.Content = FindResource("IconPause");
                    }
                }
                catch (Exception ex) { Debug.WriteLine("Fallback error: " + ex.Message); }
            }
        }

        private void MostrarControles(bool visible)
        {
            if (ControlsPanel == null) return;
            ControlsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ActualizarBordesMiniaturas(int index)
        {
            var borders = new[] { BorderThumb0, BorderThumb1, BorderThumb2, BorderThumb3 };
            foreach (var b in borders) b.BorderThickness = new Thickness(0);

            int thumbIdx = index == 0 ? 0 : index - 1;
            if (thumbIdx >= 0 && thumbIdx < borders.Length)
                borders[thumbIdx].BorderThickness = new Thickness(2);
        }

        private async void SeleccionarMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                int index = tag switch
                {
                    "bg0" => 1,
                    "bg1" => 2,
                    "bg2" => 3,
                    "bg3" => 4,
                    _ => 0
                };

                if (index < _mediaSources.Count && _mediaSources[index] != null)
                    await CambiarMedia(index);
            }
        }

        private async void btnPrev_Click(object sender, RoutedEventArgs e)
        {
            int newIdx = _currentIndex;
            for (int i = 0; i < _mediaSources.Count; i++)
            {
                newIdx--;
                if (newIdx < 0) newIdx = _mediaSources.Count - 1;
                if (_mediaSources[newIdx] != null) break;
            }
            await CambiarMedia(newIdx);
        }

        private async void btnNext_Click(object sender, RoutedEventArgs e)
        {
            int newIdx = _currentIndex;
            for (int i = 0; i < _mediaSources.Count; i++)
            {
                newIdx = (newIdx + 1) % _mediaSources.Count;
                if (_mediaSources[newIdx] != null) break;
            }
            await CambiarMedia(newIdx);
        }

        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                btnPlayPause.Content = FindResource("IconPlay");
            }
            else
            {
                _mediaPlayer.Play();
                btnPlayPause.Content = FindResource("IconPause");
            }
        }

        private void btnMute_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer.Mute = !_mediaPlayer.Mute;
        }

        private void TimelineSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
            => _isDraggingSlider = true;

        private void TimelineSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _mediaPlayer.Position = (float)(TimelineSlider.Value / 100.0);
            _isDraggingSlider = false;
        }

        // --- Auto-hide controls ---

        private void PlayerBorder_MouseEnter(object sender, MouseEventArgs e) { }
        private void PlayerBorder_MouseMove(object sender, MouseEventArgs e) { }
        private void PlayerBorder_MouseLeave(object sender, MouseEventArgs e) { }
        private void ControlsPanel_MouseEnter(object sender, MouseEventArgs e) { }
        private void ControlsPanel_MouseMove(object sender, MouseEventArgs e) { }

        // --- Scroll ---

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double offset = MainScrollViewer.VerticalOffset - e.Delta;
            offset = Math.Max(0, Math.Min(offset, MainScrollViewer.ScrollableHeight));
            MainScrollViewer.ScrollToVerticalOffset(offset);
            e.Handled = true;
        }

        private void MainScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            double offset = MainScrollViewer.VerticalOffset;
            if (e.Key == Key.Up) offset = Math.Max(0, offset - 40);
            if (e.Key == Key.Down) offset = Math.Min(MainScrollViewer.ScrollableHeight, offset + 40);
            MainScrollViewer.ScrollToVerticalOffset(offset);
            e.Handled = true;
        }

        // --- Stars ---

        private void ConfigurarEstrellas()
        {
            var stars = new[] { StarBtn1, StarBtn2, StarBtn3, StarBtn4, StarBtn5 };
            for (int i = 0; i < stars.Length; i++)
            {
                int idx = i + 1;
                var btn = stars[i];

                btn.MouseEnter += (s, e) =>
                {
                    for (int j = 0; j < stars.Length; j++)
                    {
                        bool filled = j < idx;
                        stars[j].Content = filled ? "★" : "☆";
                        stars[j].Foreground = filled
                            ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                            : new SolidColorBrush(Color.FromRgb(136, 142, 158));
                    }
                };

                btn.Tag = idx.ToString();
                btn.MouseLeave += (s, e) => MostrarEstrellas(_userVote);
                btn.Click += (s, e) => { _userVote = idx; MostrarEstrellas(idx); _ = EnviarVotoAsync(idx); };
            }
        }

        private void MostrarEstrellas(int hasta)
        {
            var stars = new[] { StarBtn1, StarBtn2, StarBtn3, StarBtn4, StarBtn5 };
            for (int j = 0; j < stars.Length; j++)
            {
                bool filled = j < hasta;
                stars[j].Content = filled ? "★" : "☆";
                stars[j].Foreground = filled
                    ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                    : new SolidColorBrush(Color.FromRgb(136, 142, 158));
            }
        }

        private async void Vote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int val))
                await EnviarVotoAsync(val);
        }

        private async Task EnviarVotoAsync(int valor)
        {
            if (!_session.IsLoggedIn)
            {
                MessageBox.Show("Debes iniciar sesión para votar.", "Login requerido",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_game == null) return;
            try
            {
                bool ok = await _voteRepo.VoteAsync(_game.id, "game",
                    _session.CurrentUser!.id, valor);
                if (ok)
                {
                    var (avg, cnt) = await _voteRepo.GetAverageRatingAsync(
                        _game.id, "game");
                    txtRatingInfo.Text = $"Media: {avg:F2} ({cnt} votos)";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error voting: " + ex.Message);
                MessageBox.Show("No se pudo registrar el voto.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null || string.IsNullOrWhiteSpace(_game.link_descarga)) return;

            var originalContent = btnDownload.Content;
            btnDownload.IsEnabled = false;
            btnDownload.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "⏳", FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Resolviendo...", FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                }
            };

            try
            {
                var (directUrl, fileName, fileSize) = await _mediaFire.ResolveAsync(_game.link_descarga);

                if (string.IsNullOrEmpty(directUrl))
                {
                    MessageBox.Show("No se pudo resolver el enlace de descarga.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    FileName = fileName ?? $"{_game.nombre ?? "juego"}.rar",
                    Filter = "Todos los archivos|*.*"
                };

                if (saveDialog.ShowDialog() != true) return;

                var savedName = Path.GetFileName(saveDialog.FileName);
                var item = new DownloadItem
                {
                    Id = Guid.NewGuid(),
                    GameId = _game.id,
                    Url = _game.link_descarga,
                    DirectUrl = directUrl,
                    FileName = savedName,
                    FileSize = fileSize,
                    Status = "ready",
                    ImageUrl = _game.image_url
                };

                await _downloadRepo.CreateAsync(item);

                DescargasView.SetPendingDownload(item.Id, saveDialog.FileName);

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.CargarDescargasView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error en descarga: " + ex.Message);
                btnDownload.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "⚠", FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "Error", FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
                MessageBox.Show("Error al iniciar descarga: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                await Task.Delay(1500);
            }
            finally
            {
                btnDownload.Content = originalContent;
                btnDownload.IsEnabled = true;
            }
        }

        public void Dispose()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            _mediaPlayer = null;
            _libVLC = null;
        }
    }
}
