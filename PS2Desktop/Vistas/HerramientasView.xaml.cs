using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PS2Desktop.Services;

namespace PS2Desktop.Vistas
{
    public partial class HerramientasView : UserControl
    {
        public event EventHandler Volver;

        private string _selectedIsoPath;
        private string _selectedBinPath;
        private string _selectedBinOutputPath;
        private string _selectedArtOutputPath;
        private CancellationTokenSource _isoCts;
        private CancellationTokenSource _binCts;

        public HerramientasView()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadDrives();
        }

        private void LoadDrives()
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
                .Select(d => new { Text = $"{d.Name} ({d.VolumeLabel})", Tag = d.Name.TrimEnd('\\') })
                .ToList();

            CmbIsoDrive.ItemsSource = drives;
            CmbCfgDrive.ItemsSource = drives;
            CmbFileDrive.ItemsSource = drives;
            CmbFat32Drive.ItemsSource = drives;

            if (drives.Count > 0)
            {
                CmbIsoDrive.SelectedIndex = 0;
                CmbCfgDrive.SelectedIndex = 0;
                CmbFileDrive.SelectedIndex = 0;
                CmbFat32Drive.SelectedIndex = 0;
            }
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            PanelIsoToUl.Visibility = TabIsoToUl.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelBinToIso.Visibility = TabBinToIso.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelUlCfg.Visibility = TabUlCfg.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelArt.Visibility = TabArt.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelArchivos.Visibility = TabArchivos.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelFat32.Visibility = TabFat32.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== ISO → UL =====

        private void BtnSelectIso_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Archivos ISO|*.iso|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _selectedIsoPath = dlg.FileName;
                LblIsoPath.Text = Path.GetFileName(dlg.FileName);

                try
                {
                    using (var iso = new ISOReader())
                    {
                        long size = iso.Init(dlg.FileName);
                        string gameId = iso.GetGameId();
                        bool isCD = iso.IsCD();

                        TxtIsoGameId.Text = gameId ?? "No detectado";
                        TxtIsoMediaType.Text = isCD ? "CD" : "DVD";

                        if (string.IsNullOrEmpty(TxtIsoGameName.Text))
                            TxtIsoGameName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);

                        IsoInfoCard.Visibility = Visibility.Visible;
                        TxtIsoInfo.Text = $"Tamaño: {OPLService.FormatSize(size)} | " +
                                          $"Partes UL: {(size + 1073741823) / 1073741824} | " +
                                          $"Game ID: {gameId ?? "N/A"}";
                    }
                }
                catch (Exception ex)
                {
                    IsoInfoCard.Visibility = Visibility.Visible;
                    TxtIsoInfo.Text = $"Error al leer ISO: {ex.Message}";
                }
            }
        }

        private async void BtnConvertIso_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedIsoPath) || !File.Exists(_selectedIsoPath))
            {
                MessageBox.Show("Selecciona un archivo ISO válido.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string gameName = TxtIsoGameName.Text.Trim();
            if (string.IsNullOrEmpty(gameName))
            {
                MessageBox.Show("Ingresa el nombre del juego.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string drive = CmbIsoDrive.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(drive))
            {
                MessageBox.Show("Selecciona una unidad de destino.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string gameId = TxtIsoGameId.Text;
            if (string.IsNullOrEmpty(gameId) || gameId.StartsWith("No detectado"))
            {
                gameId = Path.GetFileNameWithoutExtension(_selectedIsoPath).ToUpper();
            }

            if (OPLService.CheckGameExists(drive, gameName, gameId))
            {
                var result = MessageBox.Show(
                    $"Ya existe un juego con ID '{gameId}' en la unidad.\n¿Deseas continuar de todos modos?",
                    "Juego duplicado", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            _isoCts = new CancellationTokenSource();
            BtnConvertIso.IsEnabled = false;
            BtnCancelIso.Visibility = Visibility.Visible;
            IsoProgressPanel.Visibility = Visibility.Visible;

            var progress = new Progress<OPLService.ConversionProgress>(p =>
            {
                TxtIsoProgress.Text = $"Parte {p.CurrentPart}/{p.TotalParts}";
                TxtIsoPercent.Text = $"{p.PercentComplete:F1}%";
                TxtIsoStatus.Text = p.StatusMessage;

                double maxWidth = 400;
                IsoProgressBar.Width = maxWidth * p.PercentComplete / 100;
            });

            try
            {
                await Task.Run(() => OPLService.ConvertIsoToUlAsync(
                    _selectedIsoPath, drive, gameName, gameId, progress, _isoCts.Token));

                IsoInfoCard.Visibility = Visibility.Visible;
                TxtIsoInfo.Text = $"✓ '{gameName}' convertido exitosamente a formato UL en {drive}:\\";
                ToastService.Instance?.Show("Conversión completada", ToastType.Success);
            }
            catch (OperationCanceledException)
            {
                TxtIsoStatus.Text = "Conversión cancelada";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la conversión:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnConvertIso.IsEnabled = true;
                BtnCancelIso.Visibility = Visibility.Collapsed;
                _isoCts?.Dispose();
                _isoCts = null;
            }
        }

        private void BtnCancelIso_Click(object sender, RoutedEventArgs e) => _isoCts?.Cancel();

        // ===== BIN → ISO =====

        private void BtnSelectBin_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Archivos BIN|*.bin|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _selectedBinPath = dlg.FileName;
                LblBinPath.Text = Path.GetFileName(dlg.FileName);

                if (string.IsNullOrEmpty(_selectedBinOutputPath))
                {
                    _selectedBinOutputPath = Path.ChangeExtension(dlg.FileName, ".iso");
                    LblBinOutput.Text = Path.GetFileName(_selectedBinOutputPath);
                }
            }
        }

        private void BtnSelectBinOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Archivos ISO|*.iso",
                FileName = Path.GetFileNameWithoutExtension(_selectedBinPath ?? "output") + ".iso"
            };
            if (dlg.ShowDialog() == true)
            {
                _selectedBinOutputPath = dlg.FileName;
                LblBinOutput.Text = Path.GetFileName(dlg.FileName);
            }
        }

        private async void BtnConvertBin_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedBinPath) || !File.Exists(_selectedBinPath))
            {
                MessageBox.Show("Selecciona un archivo BIN válido.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedBinOutputPath))
            {
                _selectedBinOutputPath = Path.ChangeExtension(_selectedBinPath, ".iso");
            }

            _binCts = new CancellationTokenSource();
            BtnConvertBin.IsEnabled = false;
            BtnCancelBin.Visibility = Visibility.Visible;
            BinProgressPanel.Visibility = Visibility.Visible;

            var progress = new Progress<OPLService.ConversionProgress>(p =>
            {
                TxtBinProgress.Text = p.StatusMessage;
                TxtBinPercent.Text = $"{p.PercentComplete:F1}%";
                TxtBinStatus.Text = $"{OPLService.FormatSize(p.BytesWritten)} / {OPLService.FormatSize(p.TotalBytes)}";

                double maxWidth = 400;
                BinProgressBar.Width = maxWidth * p.PercentComplete / 100;
            });

            try
            {
                await Task.Run(() => OPLService.ConvertBinToIsoAsync(
                    _selectedBinPath, _selectedBinOutputPath, progress, _binCts.Token));

                ToastService.Instance?.Show("BIN convertido a ISO exitosamente", ToastType.Success);
            }
            catch (OperationCanceledException)
            {
                TxtBinStatus.Text = "Conversión cancelada";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la conversión:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnConvertBin.IsEnabled = true;
                BtnCancelBin.Visibility = Visibility.Collapsed;
                _binCts?.Dispose();
                _binCts = null;
            }
        }

        private void BtnCancelBin_Click(object sender, RoutedEventArgs e) => _binCts?.Cancel();

        // ===== ul.cfg =====

        private void BtnLoadCfg_Click(object sender, RoutedEventArgs e)
        {
            string drive = CmbCfgDrive.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(drive)) return;

            try
            {
                var games = OPLService.ReadUlCfg(drive);
                var items = games.Select(g => new
                {
                    g.Name,
                    g.GameId,
                    g.Parts,
                    Media = g.Media == 0x12 ? "CD" : "DVD",
                    SizeDisplay = OPLService.FormatSize(g.SizeBytes)
                }).ToList();

                DgUlGames.ItemsSource = items;
                TxtCfgStatus.Text = $"{items.Count} juego(s) encontrados";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al leer ul.cfg:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DgUlGames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BtnDeleteGame.IsEnabled = DgUlGames.SelectedItem != null;
        }

        private void BtnDeleteGame_Click(object sender, RoutedEventArgs e)
        {
            if (DgUlGames.SelectedItem == null) return;

            var item = DgUlGames.SelectedItem;
            string name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
            string gameId = item.GetType().GetProperty("GameId")?.GetValue(item)?.ToString();
            int parts = Convert.ToInt32(item.GetType().GetProperty("Parts")?.GetValue(item));

            var result = MessageBox.Show(
                $"¿Eliminar '{name}' ({gameId})?\nSe eliminarán {parts} archivo(s) UL.",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            string drive = CmbCfgDrive.SelectedValue?.ToString();
            try
            {
                OPLService.DeleteGame(drive, name, gameId, parts);
                BtnLoadCfg_Click(sender, e);
                ToastService.Instance?.Show($"'{name}' eliminado", ToastType.Success);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== ART Download =====

        private async void BtnDescargarArt_Click(object sender, RoutedEventArgs e)
        {
            string gameId = TxtArtGameId.Text.Trim();
            if (string.IsNullOrEmpty(gameId))
            {
                MessageBox.Show("Ingresa el Game ID del juego.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedArtOutputPath))
            {
                var dlg = new OpenFolderDialog { Title = "Selecciona carpeta destino para el ART" };
                if (dlg.ShowDialog() == true)
                {
                    _selectedArtOutputPath = dlg.FolderName;
                    LblArtOutput.Text = _selectedArtOutputPath;
                }
                else return;
            }

            BtnDescargarArt.IsEnabled = false;
            ArtProgressPanel.Visibility = Visibility.Visible;

            var progress = new Progress<double>(p =>
            {
                double maxWidth = 400;
                ArtProgressBar.Width = maxWidth * p / 100;
                TxtArtPercent.Text = $"{p:F0}%";

                if (p < 33) TxtArtStatus.Text = "Descargando COV...";
                else if (p < 66) TxtArtStatus.Text = "Descargando ICO...";
                else TxtArtStatus.Text = "Descargando BG...";
            });

            try
            {
                await OPLService.DownloadArtAsync(gameId, _selectedArtOutputPath, progress);
                ToastService.Instance?.Show("ART descargado exitosamente", ToastType.Success);
                TxtArtStatus.Text = "Descarga completada";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al descargar ART:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnDescargarArt.IsEnabled = true;
            }
        }

        private void BtnSelectArtOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Selecciona carpeta para el ART" };
            if (dlg.ShowDialog() == true)
            {
                _selectedArtOutputPath = dlg.FolderName;
                LblArtOutput.Text = _selectedArtOutputPath;
            }
        }

        // ===== File Manager =====

        private void BtnExploreFiles_Click(object sender, RoutedEventArgs e)
        {
            string drive = CmbFileDrive.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(drive)) return;

            try
            {
                var files = new System.Collections.Generic.List<object>();
                string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
                var dirInfo = new DirectoryInfo(drivePath);

                foreach (var file in dirInfo.GetFiles("ul.*"))
                {
                    string ext = Path.GetExtension(file.Name);
                    if (ext == ".cfg")
                    {
                        files.Add(new
                        {
                            Name = file.Name,
                            Type = "CFG",
                            SizeDisplay = OPLService.FormatSize(file.Length)
                        });
                    }
                    else
                    {
                        files.Add(new
                        {
                            Name = file.Name,
                            Type = "UL",
                            SizeDisplay = OPLService.FormatSize(file.Length)
                        });
                    }
                }

                foreach (var file in dirInfo.GetFiles("*.iso"))
                {
                    files.Add(new
                    {
                        Name = file.Name,
                        Type = "ISO",
                        SizeDisplay = OPLService.FormatSize(file.Length)
                    });
                }

                LvFiles.ItemsSource = files;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al explorar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRenameFile_Click(object sender, RoutedEventArgs e)
        {
            if (LvFiles.SelectedItem == null) return;
            var item = LvFiles.SelectedItem;
            string name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();

            var dlg = new InputDialog("Renombrar archivo", $"Nuevo nombre para '{name}':", name);
            if (dlg.ShowDialog() == true)
            {
                string drive = CmbFileDrive.SelectedValue?.ToString();
                string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
                string oldPath = Path.Combine(drivePath, name);
                string newPath = Path.Combine(drivePath, dlg.InputValue);

                try
                {
                    File.Move(oldPath, newPath);
                    BtnExploreFiles_Click(sender, e);
                    ToastService.Instance?.Show("Archivo renombrado", ToastType.Success);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al renombrar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDeleteFile_Click(object sender, RoutedEventArgs e)
        {
            if (LvFiles.SelectedItem == null) return;
            var item = LvFiles.SelectedItem;
            string name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();

            var result = MessageBox.Show(
                $"¿Eliminar '{name}'?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            string drive = CmbFileDrive.SelectedValue?.ToString();
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
            string filePath = Path.Combine(drivePath, name);

            try
            {
                File.Delete(filePath);
                BtnExploreFiles_Click(sender, e);
                ToastService.Instance?.Show("Archivo eliminado", ToastType.Success);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== FAT32 =====

        private async void BtnFormatFat32_Click(object sender, RoutedEventArgs e)
        {
            string drive = CmbFat32Drive.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(drive))
            {
                MessageBox.Show("Selecciona una unidad.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"¿Estás seguro de formatear {drive} a FAT32?\n\n⚠ TODOS LOS DATOS SERÁN ELIMINADOS PERMANENTEMENTE.",
                "Confirmar formateo", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var result2 = MessageBox.Show(
                "Última oportunidad de cancelar. ¿Continuar?",
                "Última confirmación", MessageBoxButton.YesNo, MessageBoxImage.Stop);

            if (result2 != MessageBoxResult.Yes) return;

            BtnFormatFat32.IsEnabled = false;
            Fat32ProgressPanel.Visibility = Visibility.Visible;

            try
            {
                string label = TxtFat32Label.Text.Trim();
                string volLabel = string.IsNullOrEmpty(label) ? "PS2_OPL" : label;

                await Task.Run(() =>
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "format.com",
                        Arguments = $"{drive} /FS:FAT32 /V:{volLabel} /Q /Y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                    }
                });

                TxtFat32Status.Text = "Formato completado";
                ToastService.Instance?.Show($"Unidad {drive} formateada a FAT32", ToastType.Success);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al formatear:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnFormatFat32.IsEnabled = true;
            }
        }
    }

    public class InputDialog : Window
    {
        public string InputValue { get; private set; }
        private TextBox _textBox;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title = title;
            Width = 400;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A2E"));
            ResizeMode = ResizeMode.NoResize;

            var stack = new StackPanel { Margin = new Thickness(20) };

            var label = new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888E9E")),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(label);

            _textBox = new TextBox
            {
                Text = defaultValue,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121212")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2F42")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 14
            };
            _textBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) { InputValue = _textBox.Text; DialogResult = true; }
                if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
            };
            stack.Children.Add(_textBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };

            var btnOk = new Button
            {
                Content = "Aceptar",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078F2")),
                Foreground = Brushes.White,
                Width = 90,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnOk.Click += (s, e) => { InputValue = _textBox.Text; DialogResult = true; };
            btnPanel.Children.Add(btnOk);

            var btnCancel = new Button
            {
                Content = "Cancelar",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2F42")),
                Foreground = Brushes.White,
                Width = 90,
                Height = 32,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnCancel.Click += (s, e) => DialogResult = false;
            btnPanel.Children.Add(btnCancel);

            stack.Children.Add(btnPanel);
            Content = stack;

            Loaded += (s, e) => _textBox.Focus();
        }
    }
}
