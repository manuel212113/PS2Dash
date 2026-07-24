using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;

        // UL Tab state
        private string _ulCurrentDrive = "";
        private string _ulSystemFilter = "ALL";
        private string _ulSearchText = "";
        private string _ulSortBy = "date_desc";
        private bool _ulIsGridView = true;
        private int _ulCurrentPage = 1;
        private int _ulTotalPages = 1;
        private const int UlGamesPerPage = 60;
        private ObservableCollection<UlGameRow> _ulGames = new();
        private UlGameRow? _ulSelectedGame;
        private CancellationTokenSource? _ulSearchCts;

        // Import Tab state
        private string _importRootPath = "";
        private List<ImportFileInfo> _importFiles = new();
        private string _importPs2CdPath = "";
        private string _importPs1Path = "";
        private string _importAppsPath = "";

        public class ImportFileInfo
        {
            public string FilePath { get; set; } = "";
            public string FileName => Path.GetFileName(FilePath);
            public string GameId { get; set; } = "";
            public string SizeDisplay => OPLService.FormatSize(new FileInfo(FilePath).Length);
        }

        public class UlGameRow
        {
            public string Name { get; set; }
            public string GameId { get; set; }
            public int Parts { get; set; }
            public string MediaLabel { get; set; }
            public string SizeDisplay { get; set; }
            public string CoverPath { get; set; }
            public string IconPath { get; set; }
            public string BackgroundPath { get; set; }
            public string ScreenshotPath { get; set; }
            public string[] ScreenshotPaths { get; set; }
        }

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
            CmbFileDrive.ItemsSource = drives;
            CmbFat32Drive.ItemsSource = drives;

            if (drives.Count > 0)
            {
                CmbIsoDrive.SelectedIndex = 0;
                CmbFileDrive.SelectedIndex = 0;
                CmbFat32Drive.SelectedIndex = 0;
                _ulCurrentDrive = drives[0].Tag as string ?? "";
            }

            // Initialize UL drive selector
            CmbUlDrive.ItemsSource = drives;
            if (drives.Count > 0)
            {
                CmbUlDrive.SelectedIndex = 0;
            }

            // Initialize UL system tabs
            InitializeUlSystemTabs();
        }

        private void CmbUlDrive_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbUlDrive.SelectedValue is string drive)
            {
                _ulCurrentDrive = drive;
                _ulCurrentPage = 1;
                if (TabUlCfg.IsChecked == true)
                {
                    _ = LoadUlGamesAsync();
                }
            }
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            PanelImportar.Visibility = TabImportar.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelIsoToUl.Visibility = TabIsoToUl.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelBinToIso.Visibility = TabBinToIso.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelUlCfg.Visibility = TabUlCfg.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelArt.Visibility = TabArt.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelArchivos.Visibility = TabArchivos.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelFat32.Visibility = TabFat32.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            // Load UL games when tab becomes visible
            if (TabUlCfg.IsChecked == true && !string.IsNullOrEmpty(_ulCurrentDrive))
            {
                _ = LoadUlGamesAsync();
            }
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

        private void InitializeUlSystemTabs()
        {
            var tabs = new[]
            {
                new { Label = "Todos", Value = "ALL" },
                new { Label = "PS2 DVD", Value = "DVD" },
                new { Label = "PS2 CD", Value = "CD" },
                new { Label = "PS1", Value = "PS1" },
                new { Label = "Apps", Value = "APPS" }
            };

            foreach (var tab in tabs)
            {
                var btn = new RadioButton
                {
                    Content = tab.Label,
                    Tag = tab.Value,
                    Style = (Style)FindResource("SystemTabRadioStyle"),
                    GroupName = "UlSystemTabs",
                    IsChecked = tab.Value == _ulSystemFilter,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                btn.Checked += UlSystemTab_Checked;
                UlSystemTabsPanel.Children.Add(btn);
            }
        }

        private void UlSystemTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string val)
            {
                _ulSystemFilter = val;
                _ulCurrentPage = 1;
                _ = LoadUlGamesAsync();
            }
        }

        private async void TxtUlSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtUlSearch.Text == "Buscar juegos...")
            {
                TxtUlSearch.Text = "";
                TxtUlSearch.Foreground = (Brush)FindResource("TextMainBrush");
            }
        }

        private void TxtUlSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtUlSearch.Text))
            {
                TxtUlSearch.Text = "Buscar juegos...";
                TxtUlSearch.Foreground = (Brush)FindResource("TextMutedBrush");
                _ulSearchText = null;
            }
            else
            {
                _ulSearchText = TxtUlSearch.Text.Trim();
            }
            _ulCurrentPage = 1;
            _ = LoadUlGamesAsync();
        }

        private async void TxtUlSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtUlSearch.Text == "Buscar juegos..." || string.IsNullOrWhiteSpace(TxtUlSearch.Text))
            {
                _ulSearchCts?.Cancel();
                return;
            }
            _ulSearchCts?.Cancel();
            _ulSearchCts = new CancellationTokenSource();
            var token = _ulSearchCts.Token;
            try
            {
                await Task.Delay(300, token);
                if (!token.IsCancellationRequested)
                {
                    _ulSearchText = TxtUlSearch.Text.Trim();
                    _ulCurrentPage = 1;
                    await LoadUlGamesAsync();
                }
            }
            catch (TaskCanceledException) { }
        }

        private void TxtUlSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && TxtUlSearch.Text != "Buscar juegos..." && !string.IsNullOrWhiteSpace(TxtUlSearch.Text))
            {
                e.Handled = true;
                _ulSearchCts?.Cancel();
                _ulSearchText = TxtUlSearch.Text.Trim();
                _ulCurrentPage = 1;
                _ = LoadUlGamesAsync();
            }
        }

        private async void CboUlSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboUlSort.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _ulSortBy = tag;
                _ulCurrentPage = 1;
                await LoadUlGamesAsync();
            }
        }

        private void BtnUlViewToggle_Checked(object sender, RoutedEventArgs e)
        {
            _ulIsGridView = true;
        }

        private void BtnUlViewToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _ulIsGridView = false;
        }

        private async void BtnUlRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadUlGamesAsync();
        }

        private async Task LoadUlGamesAsync()
        {
            if (string.IsNullOrEmpty(_ulCurrentDrive)) return;

            UlSkeletonPanel.Visibility = Visibility.Visible;
            _ulGames.Clear();
            TxtUlGamesHeader.Text = "Juegos en ul.cfg";

            try
            {
                var games = OPLService.ReadUlCfg(_ulCurrentDrive);
                string drivePath = _ulCurrentDrive.Length == 1 ? _ulCurrentDrive + ":\\" : _ulCurrentDrive + "\\";

                var items = games.Select(g =>
                {
                    string gameId = g.GameId;
                    string gameIdAlt = gameId.Replace('-', '_').Replace(".", "");

                    string coverPath = FindArtFlat(drivePath, gameId, gameIdAlt, "_COV");
                    string iconPath = FindArtFlat(drivePath, gameId, gameIdAlt, "_ICO");
                    string bgPath = FindArtFlat(drivePath, gameId, gameIdAlt, "_BG");
                    string shotPath = FindArtFlat(drivePath, gameId, gameIdAlt, "_SCR");
                    string shotPath2 = FindArtFlat(drivePath, gameId, gameIdAlt, "_SCR2");

                    var shots = new List<string>();
                    if (!string.IsNullOrEmpty(shotPath)) shots.Add(shotPath);
                    if (!string.IsNullOrEmpty(shotPath2)) shots.Add(shotPath2);

                    return new UlGameRow
                    {
                        Name = g.Name,
                        GameId = g.GameId,
                        Parts = g.Parts,
                        MediaLabel = g.Media == 0x12 ? "CD" : "DVD",
                        SizeDisplay = OPLService.FormatSize(g.SizeBytes),
                        CoverPath = coverPath,
                        IconPath = iconPath,
                        BackgroundPath = bgPath,
                        ScreenshotPath = shotPath,
                        ScreenshotPaths = shots.ToArray()
                    };
                }).ToList();

                // Apply system filter
                if (_ulSystemFilter != "ALL")
                {
                    items = items.Where(x => x.MediaLabel == _ulSystemFilter || 
                        (_ulSystemFilter == "APPS" && x.MediaLabel == "APP")).ToList();
                }

                // Apply search
                if (!string.IsNullOrWhiteSpace(_ulSearchText))
                {
                    var search = _ulSearchText.ToLowerInvariant();
                    items = items.Where(g => 
                        g.Name.ToLower().Contains(search) ||
                        g.GameId.ToLower().Contains(search)
                    ).ToList();
                }

                // Apply sort
                items = _ulSortBy switch
                {
                    "date_desc" => items.OrderByDescending(g => g.GameId).ToList(),
                    "date_asc" => items.OrderBy(g => g.GameId).ToList(),
                    "name_asc" => items.OrderBy(g => g.Name).ToList(),
                    "name_desc" => items.OrderByDescending(g => g.Name).ToList(),
                    _ => items
                };

                _ulTotalPages = Math.Max(1, (int)Math.Ceiling((double)items.Count / UlGamesPerPage));
                if (_ulCurrentPage > _ulTotalPages) _ulCurrentPage = _ulTotalPages;
                if (_ulCurrentPage < 1) _ulCurrentPage = 1;

                var pageItems = items.Skip((_ulCurrentPage - 1) * UlGamesPerPage).Take(UlGamesPerPage).ToList();

                // Bind to ListBox
                _ulGames.Clear();
                foreach (var vm in pageItems) _ulGames.Add(vm);

                // Select first if none selected
                if (_ulSelectedGame == null || !_ulGames.Contains(_ulSelectedGame))
                {
                    if (_ulGames.Count > 0)
                    {
                        UlGamesListBox.SelectedIndex = 0;
                    }
                }

                // Update pagination
                TxtUlPageInfo.Text = _ulCurrentPage.ToString();
                TxtUlTotalPages.Text = _ulTotalPages.ToString();
                BtnUlPrevPage.IsEnabled = _ulCurrentPage > 1;
                BtnUlNextPage.IsEnabled = _ulCurrentPage < _ulTotalPages;
                TxtUlGameCount.Text = $"{items.Count} juego{(items.Count != 1 ? "s" : "")}";

                // Load images
                await LoadUlImagesAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error cargando ul.cfg: {ex.Message}");
            }
            finally
            {
                UlSkeletonPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadUlImagesAsync()
        {
            var maxConcurrent = AppSettings.ImageConcurrency;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();

            foreach (var vm in _ulGames)
            {
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var primary = await ImageCacheService.Instance.GetImageAsync(vm.CoverPath);
                        if (primary == null) return;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            var lbi = UlGamesListBox.ItemContainerGenerator.ContainerFromItem(vm) as ListBoxItem;
                            if (lbi != null && lbi.ContentTemplate.FindName("CoverImg", lbi) is Image img)
                            {
                                img.Source = primary;
                                img.Opacity = 1;
                            }
                        });
                    }
                    catch { }
                    finally { semaphore.Release(); }
                }));
            }
            try { await Task.WhenAll(tasks); }
            catch { }
        }

        private void OnUlGameSelected(UlGameRow vm)
        {
            _ulSelectedGame = vm;
            UlGridContainer.Visibility = Visibility.Collapsed;
            UlDetailOverlay.Visibility = Visibility.Visible;
            TxtUlDetailTitle.Text = vm.Name;

            // Populate detail fields
            DetailName.Text = vm.Name;
            DetailMedia.Text = vm.MediaLabel;
            DetailGameId.Text = vm.GameId;
            DetailRegion.Text = "";
            DetailParts.Text = vm.Parts.ToString();
            DetailSize.Text = vm.SizeDisplay;

            // Cover
            if (!string.IsNullOrEmpty(vm.CoverPath) && System.IO.File.Exists(vm.CoverPath))
                DetailCover.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(vm.CoverPath));
            else
                DetailCover.Source = null;

            // Icon
            if (!string.IsNullOrEmpty(vm.IconPath) && System.IO.File.Exists(vm.IconPath))
                DetailIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(vm.IconPath));
            else
                DetailIcon.Source = null;

            SetDetailBackground(UlDetailBg, vm.BackgroundPath, vm.CoverPath);
            UlGamesListBox.SelectedItem = vm;
        }

        private void UlGamesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UlGamesListBox.SelectedItem is UlGameRow vm)
                OnUlGameSelected(vm);
        }

        private void UlGamesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UlGamesListBox.SelectedItem is UlGameRow vm)
                OnUlGameSelected(vm);
        }

        private void BtnUlDetailBack_Click(object sender, RoutedEventArgs e)
        {
            UlDetailOverlay.Visibility = Visibility.Collapsed;
            UlGridContainer.Visibility = Visibility.Visible;
            UlGamesListBox.SelectedItem = null;
        }

        private void BtnUlPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_ulCurrentPage > 1)
            {
                _ulCurrentPage--;
                _ = LoadUlGamesAsync();
            }
        }

        private void BtnUlNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_ulCurrentPage < _ulTotalPages)
            {
                _ulCurrentPage++;
                _ = LoadUlGamesAsync();
            }
        }

        private async void BtnDeleteGame_Click(object sender, RoutedEventArgs e)
        {
            if (_ulSelectedGame == null) return;
            var result = MessageBox.Show(
                $"¿Eliminar '{_ulSelectedGame.Name}' ({_ulSelectedGame.GameId})?\nSe eliminarán {_ulSelectedGame.Parts} archivo(s) UL.",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                OPLService.DeleteGame(_ulCurrentDrive, _ulSelectedGame.Name, _ulSelectedGame.GameId, _ulSelectedGame.Parts);
                ToastService.Instance?.ShowSuccess($"'{_ulSelectedGame.Name}' eliminado");
                await LoadUlGamesAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
        }

        private async void BtnRenameGame_Click(object sender, RoutedEventArgs e)
        {
            if (_ulSelectedGame == null) return;
            var dlg = new InputDialog("Renombrar juego", $"Nuevo nombre para '{_ulSelectedGame.Name}':", _ulSelectedGame.Name);
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputValue)) return;

            string newName = dlg.InputValue.Trim();
            if (newName == _ulSelectedGame.Name) return;

            var result = MessageBox.Show(
                $"Renombrar '{_ulSelectedGame.Name}' → '{newName}'?\nSe actualizará el ul.cfg y los archivos .ul.*",
                "Confirmar renombrado", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                OPLService.RenameGame(_ulCurrentDrive, _ulSelectedGame.Name, newName, _ulSelectedGame.GameId, _ulSelectedGame.Parts);
                ToastService.Instance?.ShowSuccess($"Renombrado a '{newName}'");
                await LoadUlGamesAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
        }

        private void BtnExportArt_Click(object sender, RoutedEventArgs e)
        {
            if (_ulSelectedGame == null) return;
            var dlg = new OpenFolderDialog { Title = "Selecciona carpeta destino para exportar ART" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    int count = 0;
                    if (!string.IsNullOrEmpty(_ulSelectedGame.CoverPath))
                    {
                        File.Copy(_ulSelectedGame.CoverPath, Path.Combine(dlg.FolderName, $"{_ulSelectedGame.GameId}_COV.jpg"), true);
                        count++;
                    }
                    if (!string.IsNullOrEmpty(_ulSelectedGame.IconPath))
                    {
                        File.Copy(_ulSelectedGame.IconPath, Path.Combine(dlg.FolderName, $"{_ulSelectedGame.GameId}_ICO.png"), true);
                        count++;
                    }
                    if (_ulSelectedGame.ScreenshotPaths != null)
                    {
                        for (int i = 0; i < _ulSelectedGame.ScreenshotPaths.Length; i++)
                        {
                            File.Copy(_ulSelectedGame.ScreenshotPaths[i], Path.Combine(dlg.FolderName, $"{_ulSelectedGame.GameId}_SCR{i+1}.jpg"), true);
                            count++;
                        }
                    }
                    ToastService.Instance?.ShowSuccess($"Exportados {count} archivos ART");
                }
                catch (Exception ex)
                {
                    ToastService.Instance?.ShowError($"Error exportando: {ex.Message}");
                }
            }
        }

        private string FindArtFlat(string driveRoot, string gameId, string gameIdAlt, string suffix)
        {
            string artDir = Path.Combine(driveRoot, "ART");
            if (!Directory.Exists(artDir)) return "";

            string[] exts = { ".jpg", ".png", ".bmp" };
            foreach (var id in new[] { gameId, gameIdAlt })
            {
                foreach (var ext in exts)
                {
                    string path = Path.Combine(artDir, $"{id}{suffix}{ext}");
                    if (File.Exists(path)) return path;
                    if (suffix == "_COV" || suffix == "_SCR")
                    {
                        string path2 = Path.Combine(artDir, $"{id}{suffix}2{ext}");
                        if (File.Exists(path2)) return path2;
                    }
                }
            }
            return "";
        }

        private void SetDetailBackground(System.Windows.Controls.Image bgImage, string bgPath, string coverPath)
        {
            string path = !string.IsNullOrEmpty(bgPath) && File.Exists(bgPath) ? bgPath
                        : !string.IsNullOrEmpty(coverPath) && File.Exists(coverPath) ? coverPath
                        : "";

            if (string.IsNullOrEmpty(path))
            {
                bgImage.Source = null;
                bgImage.Effect = null;
                return;
            }

            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 800;
                bitmap.EndInit();
                bitmap.Freeze();

                bgImage.Source = bitmap;
                bgImage.Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = 40,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian
                };
            }
            catch { bgImage.Source = null; bgImage.Effect = null; }
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

        // ===== Import Tab =====

        private void TabImport_Click(object sender, RoutedEventArgs e)
        {
            PanelImportPs2Dvd.Visibility = TabImportPs2Dvd.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelImportPs2Cd.Visibility = TabImportPs2Cd.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelImportPs1.Visibility = TabImportPs1.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelImportApps.Visibility = TabImportApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnImportSelectRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Selecciona la carpeta raíz de OPL" };
            if (dlg.ShowDialog() == true)
            {
                _importRootPath = dlg.FolderName;
                LblImportRoot.Text = _importRootPath;

                if (!OPLLibraryService.IsOplRoot(_importRootPath))
                {
                    var result = MessageBox.Show(
                        "Esta carpeta no parece ser una raíz de OPL válida.\n¿Deseas crear la estructura de carpetas OPL?",
                        "Carpeta no detectada", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        OPLLibraryService.CreateOplStructure(_importRootPath);
                        ToastService.Instance?.ShowSuccess("Estructura OPL creada");
                    }
                }
            }
        }

        private void BtnImportAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Imágenes de disco|*.iso;*.zso|Todos los archivos|*.*",
                Multiselect = true,
                Title = "Selecciona archivos ISO/ZSO para importar"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                {
                    if (_importFiles.Any(f => f.FilePath == file)) continue;

                    var info = new ImportFileInfo { FilePath = file };
                    try
                    {
                        using var iso = new ISOReader();
                        iso.Init(file);
                        info.GameId = iso.GetGameId() ?? "";
                    }
                    catch { info.GameId = "N/A"; }

                    _importFiles.Add(info);
                }
                LvImportFiles.ItemsSource = null;
                LvImportFiles.ItemsSource = _importFiles;
            }
        }

        private void BtnImportClear_Click(object sender, RoutedEventArgs e)
        {
            _importFiles.Clear();
            LvImportFiles.ItemsSource = null;
        }

        private async void BtnImportStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_importRootPath))
            {
                MessageBox.Show("Selecciona la carpeta raíz de OPL.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_importFiles.Count == 0)
            {
                MessageBox.Show("Agrega archivos a importar.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnImportStart.IsEnabled = false;
            ImportProgressPanel.Visibility = Visibility.Visible;
            bool downloadArt = ChkImportDownloadArt.IsChecked == true;

            try
            {
                int total = _importFiles.Count;
                for (int i = 0; i < total; i++)
                {
                    var file = _importFiles[i];
                    double percent = (double)(i + 1) / total * 100;
                    TxtImportProgress.Text = $"Importando {i + 1}/{total}";
                    TxtImportPercent.Text = $"{percent:F0}%";
                    TxtImportStatus.Text = file.FileName;
                    double maxWidth = 400;
                    ImportProgressBar.Width = maxWidth * percent / 100;

                    string destFolder = Path.Combine(_importRootPath, "DVD");
                    string destPath = Path.Combine(destFolder, file.FileName);

                    if (!Directory.Exists(destFolder))
                        Directory.CreateDirectory(destFolder);

                    await Task.Run(() =>
                    {
                        using var src = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        src.CopyTo(dst);
                    });

                    if (downloadArt && !string.IsNullOrEmpty(file.GameId) && file.GameId != "N/A")
                    {
                        try
                        {
                            string artDir = Path.Combine(_importRootPath, "ART");
                            await OPLService.DownloadArtAsync(file.GameId, artDir, new Progress<double>());
                        }
                        catch { }
                    }
                }

                TxtImportStatus.Text = $"Importados {_importFiles.Count} juegos";
                ToastService.Instance?.ShowSuccess($"Importados {_importFiles.Count} juegos");
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error importando: {ex.Message}");
            }
            finally
            {
                BtnImportStart.IsEnabled = true;
                ImportProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnImportSelectPs2Cd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Archivos CUE|*.cue|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _importPs2CdPath = dlg.FileName;
                LblImportPs2CdPath.Text = Path.GetFileName(dlg.FileName);
            }
        }

        private async void BtnImportPs2Cd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_importRootPath) || string.IsNullOrEmpty(_importPs2CdPath))
            {
                MessageBox.Show("Selecciona la carpeta raíz y el archivo CUE.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string destFolder = Path.Combine(_importRootPath, "CD");
                if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                string isoName = Path.GetFileNameWithoutExtension(_importPs2CdPath) + ".iso";
                string destPath = Path.Combine(destFolder, isoName);

                await Task.Run(() => OPLService.ConvertBinToIsoAsync(
                    Path.ChangeExtension(_importPs2CdPath, ".bin"), destPath,
                    new Progress<OPLService.ConversionProgress>()));

                ToastService.Instance?.ShowSuccess($"PS2 CD importado a {isoName}");
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
        }

        private void BtnImportSelectPs1_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Archivos CUE|*.cue|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _importPs1Path = dlg.FileName;
                LblImportPs1Path.Text = Path.GetFileName(dlg.FileName);
            }
        }

        private async void BtnImportPs1_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_importRootPath) || string.IsNullOrEmpty(_importPs1Path))
            {
                MessageBox.Show("Selecciona la carpeta raíz y el archivo CUE.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string destFolder = Path.Combine(_importRootPath, "VCD");
                if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                string vcdName = Path.GetFileNameWithoutExtension(_importPs1Path) + ".vcd";
                string destPath = Path.Combine(destFolder, vcdName);

                string binPath = Path.ChangeExtension(_importPs1Path, ".bin");
                await Task.Run(() => OPLService.ConvertBinToIsoAsync(binPath, destPath,
                    new Progress<OPLService.ConversionProgress>()));

                ToastService.Instance?.ShowSuccess($"PS1 importado como {vcdName}");
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
        }

        private void BtnImportSelectApps_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Archivos ELF|*.elf|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _importAppsPath = dlg.FileName;
                LblImportAppsPath.Text = Path.GetFileName(dlg.FileName);
            }
        }

        private async void BtnImportApps_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_importRootPath) || string.IsNullOrEmpty(_importAppsPath))
            {
                MessageBox.Show("Selecciona la carpeta raíz y el archivo ELF.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string title = TxtImportAppsTitle.Text.Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = Path.GetFileNameWithoutExtension(_importAppsPath);
            }

            try
            {
                string appId = title.Replace(" ", "_").ToUpperInvariant();
                string appFolder = Path.Combine(_importRootPath, "APPS", appId);
                if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);

                string destPath = Path.Combine(appFolder, Path.GetFileName(_importAppsPath));
                await Task.Run(() =>
                {
                    using var src = new FileStream(_importAppsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    src.CopyTo(dst);
                });

                // Create title.cfg
                string cfgPath = Path.Combine(appFolder, "title.cfg");
                if (!File.Exists(cfgPath))
                {
                    File.WriteAllText(cfgPath, $"title={title}\n");
                }

                ToastService.Instance?.ShowSuccess($"App '{title}' importada");
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
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
