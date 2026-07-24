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
    public partial class LibreriaView : UserControl, IDisposable
    {
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private CancellationTokenSource? _disposedCts;

        // Library Tab state
        private string _libRootPath = "";
        private string _libSystemFilter = "ALL";
        private string _libSearchText = "";
        private string _libSortBy = "name_asc";
        private bool _libIsGridView = true;
        private int _libCurrentPage = 1;
        private int _libTotalPages = 1;
        private const int LibGamesPerPage = 60;
        private List<OPLLibraryService.LibraryGame> _libAllGames = new();
        private ObservableCollection<OPLLibraryService.LibraryGame> _libGames = new();
        private OPLLibraryService.LibraryGame? _libSelectedGame;
        private CancellationTokenSource? _libSearchCts;

        private RadioButton _libTabAll, _libTabPs2Dvd, _libTabPs2Cd, _libTabPs1, _libTabApps;


        private static readonly string LibRootPathFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib_root.txt");

        public LibreriaView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                InitializeLibSystemTabs();
                LoadSavedRootPath();
            };
        }

        private void LoadSavedRootPath()
        {
            try
            {
                if (System.IO.File.Exists(LibRootPathFile))
                {
                    var saved = System.IO.File.ReadAllText(LibRootPathFile).Trim();
                    if (!string.IsNullOrEmpty(saved) && System.IO.Directory.Exists(saved))
                    {
                        _libRootPath = saved;
                        LblLibRootPath.Text = _libRootPath;
                        UpdateMountState();
                        _ = ScanLibraryAsync();
                        return;
                    }
                }
            }
            catch { }
            UpdateMountState();
        }

        private void SaveRootPath()
        {
            try { System.IO.File.WriteAllText(LibRootPathFile, _libRootPath); } catch { }
        }

        private void ClearSavedRootPath()
        {
            try { if (System.IO.File.Exists(LibRootPathFile)) System.IO.File.Delete(LibRootPathFile); } catch { }
        }

        private void UpdateMountState()
        {
            if (string.IsNullOrEmpty(_libRootPath))
            {
                LibEmptyState.Visibility = Visibility.Visible;
                LibMountedState.Visibility = Visibility.Collapsed;
            }
            else
            {
                LibEmptyState.Visibility = Visibility.Collapsed;
                LibMountedState.Visibility = Visibility.Visible;
            }
        }

        private void InitializeLibSystemTabs()
        {
            var tabs = new[]
            {
                new { Label = "Todos", Value = "ALL" },
                new { Label = "PS2 DVD", Value = "PS2 DVD" },
                new { Label = "PS2 CD", Value = "PS2 CD" },
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
                    GroupName = "LibSystemTabs",
                    IsChecked = tab.Value == _libSystemFilter,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                btn.Checked += LibSystemTab_Checked;
                LibSystemTabsPanel.Children.Add(btn);

                if (tab.Value == "ALL") _libTabAll = btn;
                else if (tab.Value == "PS2 DVD") _libTabPs2Dvd = btn;
                else if (tab.Value == "PS2 CD") _libTabPs2Cd = btn;
                else if (tab.Value == "PS1") _libTabPs1 = btn;
                else if (tab.Value == "APPS") _libTabApps = btn;
            }
        }

        private void UpdateLibSystemTabCounts(int total, int ps2Dvd, int ps2Cd, int ps1, int apps)
        {
            if (_libTabAll != null) _libTabAll.Content = $"Todos ({total})";
            if (_libTabPs2Dvd != null) _libTabPs2Dvd.Content = $"PS2 DVD ({ps2Dvd})";
            if (_libTabPs2Cd != null) _libTabPs2Cd.Content = $"PS2 CD ({ps2Cd})";
            if (_libTabPs1 != null) _libTabPs1.Content = $"PS1 ({ps1})";
            if (_libTabApps != null) _libTabApps.Content = $"Apps ({apps})";
        }

        private void LibSystemTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string val)
            {
                _libSystemFilter = val;
                _libCurrentPage = 1;
                RefreshLibGames();
            }
        }

        private void BtnLibSelectRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Selecciona la carpeta raíz de OPL" };
            if (dlg.ShowDialog() == true)
            {
                _libRootPath = dlg.FolderName;
                LblLibRootPath.Text = _libRootPath;
                SaveRootPath();
                UpdateMountState();

                if (!OPLLibraryService.IsOplRoot(_libRootPath))
                {
                    var result = MessageBox.Show(
                        "Esta carpeta no parece ser una raíz de OPL válida.\n¿Deseas crear la estructura de carpetas OPL?",
                        "Carpeta no detectada", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        OPLLibraryService.CreateOplStructure(_libRootPath);
                        ToastService.Instance?.ShowSuccess("Estructura OPL creada");
                    }
                }

                _ = ScanLibraryAsync();
            }
        }

        private void BtnLibUnmount_Click(object sender, RoutedEventArgs e)
        {
            _libRootPath = "";
            _libAllGames.Clear();
            _libGames.Clear();
            TxtLibGameCount.Text = "0 juegos";
            ClearSavedRootPath();
            UpdateMountState();
        }

        private async void BtnLibScan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_libRootPath))
            {
                MessageBox.Show("Primero selecciona la carpeta raíz de OPL.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await ScanLibraryAsync();
        }

        private async Task ScanLibraryAsync()
        {
            if (string.IsNullOrEmpty(_libRootPath)) return;

            LibSkeletonPanel.Visibility = Visibility.Visible;
            LibScanProgress.Visibility = Visibility.Visible;
            BtnLibScan.IsEnabled = false;

            try
            {
                var progress = new Progress<int>(p =>
                {
                    double maxWidth = 400;
                    LibScanProgressBar.Width = maxWidth * p / 100;
                    TxtLibScanStatus.Text = $"Escaneando... {p}%";
                });

                var (games, stats) = await OPLLibraryService.ScanLibraryAsync(_libRootPath, progress);

                _libAllGames = games;

                UpdateLibSystemTabCounts(stats.TotalGames, stats.Ps2Dvd, stats.Ps2Cd, stats.Ps1, stats.Apps);
                TxtLibScanStatus.Text = $"Completado — {stats.TotalGames} juegos, {stats.TotalSizeDisplay}";

                _libCurrentPage = 1;
                RefreshLibGames();
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error escaneando: {ex.Message}");
            }
            finally
            {
                LibSkeletonPanel.Visibility = Visibility.Collapsed;
                LibScanProgress.Visibility = Visibility.Collapsed;
                BtnLibScan.IsEnabled = true;
            }
        }

        private void RefreshLibGames()
        {
            if (TxtLibPageInfo == null) return;

            var items = _libAllGames.AsEnumerable();

            if (_libSystemFilter != "ALL")
                items = items.Where(g => g.System == _libSystemFilter);

            if (!string.IsNullOrWhiteSpace(_libSearchText))
            {
                var search = _libSearchText.ToLowerInvariant();
                items = items.Where(g =>
                    g.Name.ToLower().Contains(search) ||
                    g.GameId.ToLower().Contains(search));
            }

            items = _libSortBy switch
            {
                "name_asc" => items.OrderBy(g => g.Name),
                "name_desc" => items.OrderByDescending(g => g.Name),
                "id_asc" => items.OrderBy(g => g.GameId),
                "size_desc" => items.OrderByDescending(g => g.FileSize),
                "date_desc" => items.OrderByDescending(g => g.LastModified),
                _ => items
            };

            var list = items.ToList();
            _libTotalPages = Math.Max(1, (int)Math.Ceiling((double)list.Count / LibGamesPerPage));
            if (_libCurrentPage > _libTotalPages) _libCurrentPage = _libTotalPages;
            if (_libCurrentPage < 1) _libCurrentPage = 1;

            var pageItems = list.Skip((_libCurrentPage - 1) * LibGamesPerPage).Take(LibGamesPerPage).ToList();

            _libGames.Clear();
            foreach (var vm in pageItems) _libGames.Add(vm);

            if (LibGamesListBox != null)
            {
                LibGamesListBox.ItemsSource = _libGames;
                LibGamesListBox.ItemTemplate = (DataTemplate)Resources["LibGamePosterTemplate"];

                if (_libSelectedGame == null || !_libGames.Contains(_libSelectedGame))
                {
                    if (_libGames.Count > 0)
                        LibGamesListBox.SelectedIndex = 0;
                }
            }

            TxtLibPageInfo.Text = _libCurrentPage.ToString();
            TxtLibTotalPages.Text = _libTotalPages.ToString();
            BtnLibPrevPage.IsEnabled = _libCurrentPage > 1;
            BtnLibNextPage.IsEnabled = _libCurrentPage < _libTotalPages;
            TxtLibGameCount.Text = $"{list.Count} juego{(list.Count != 1 ? "s" : "")}";

            _ = LoadLibImagesAsync();
        }

        private async Task LoadLibImagesAsync()
        {
            if (LibGamesListBox == null) return;

            var maxConcurrent = AppSettings.ImageConcurrency;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();

            foreach (var vm in _libGames)
            {
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (string.IsNullOrEmpty(vm.CoverPath)) return;
                        var primary = await ImageCacheService.Instance.GetImageAsync(vm.CoverPath);
                        if (primary == null) return;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            var lbi = LibGamesListBox.ItemContainerGenerator.ContainerFromItem(vm) as ListBoxItem;
                            if (lbi != null && lbi.ContentTemplate.FindName("CoverImg", lbi) is System.Windows.Controls.Image img)
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

        private void OnLibGameSelected(OPLLibraryService.LibraryGame vm)
        {
            _libSelectedGame = vm;
            LibGridContainer.Visibility = Visibility.Collapsed;
            LibDetailOverlay.Visibility = Visibility.Visible;
            TxtLibDetailTitle.Text = vm.Name;

            // Populate detail fields
            LibDetailName.Text = vm.Name;
            LibDetailSystem.Text = vm.System;
            LibDetailGameId.Text = vm.GameId;
            LibDetailRegion.Text = vm.Region;
            LibDetailRegionMeta.Text = vm.Region;
            LibDetailFormat.Text = vm.Format;
            LibDetailSize.Text = vm.SizeDisplay;
            LibDetailParts.Text = vm.Parts.ToString();

            // System badge color
            LibDetailSystemBadge.Background = new SolidColorBrush(
                vm.System == "PS1" ? Color.FromRgb(0x6B, 0x3F, 0xBF) :
                vm.System == "APPS" ? Color.FromRgb(0x1A, 0xBF, 0xDB) :
                Color.FromRgb(0x5B, 0x3F, 0xBF));

            // Cover
            if (!string.IsNullOrEmpty(vm.CoverPath) && System.IO.File.Exists(vm.CoverPath))
                LibDetailCover.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(vm.CoverPath));
            else
                LibDetailCover.Source = null;

            // Icon
            if (!string.IsNullOrEmpty(vm.IconPath) && System.IO.File.Exists(vm.IconPath))
                LibDetailIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(vm.IconPath));
            else
                LibDetailIcon.Source = null;

            // Screenshots
            var screenshots = new ObservableCollection<string>();
            if (vm.ScreenshotPaths != null)
            {
                foreach (var s in vm.ScreenshotPaths)
                    if (!string.IsNullOrEmpty(s) && System.IO.File.Exists(s))
                        screenshots.Add(s);
            }
            LibDetailScreenshots.ItemsSource = screenshots;

            SetDetailBackground(LibDetailBg, vm.BackgroundPath, vm.CoverPath);
            if (LibGamesListBox != null)
                LibGamesListBox.SelectedItem = vm;

            UpdateCfgButtonAndInfo(vm);
        }

        private void UpdateCfgButtonAndInfo(OPLLibraryService.LibraryGame vm)
        {
            if (string.IsNullOrEmpty(_libRootPath)) return;

            bool exists = OPLCfgDatabaseService.CfgExists(_libRootPath, vm.GameId);
            BtnLibDownloadCfg.Visibility = exists ? Visibility.Collapsed : Visibility.Visible;

            if (exists)
            {
                var cfg = OPLCfgDatabaseService.ReadLocalCfg(_libRootPath, vm.GameId);
                if (cfg != null)
                {
                    LibCfgInfoPanel.Visibility = Visibility.Visible;
                    LibCfgGenre.Text = string.IsNullOrEmpty(cfg.Genre) ? "—" : cfg.Genre;
                    LibCfgDeveloper.Text = string.IsNullOrEmpty(cfg.Developer) ? "—" : cfg.Developer;
                    LibCfgRelease.Text = string.IsNullOrEmpty(cfg.Release) ? "—" : cfg.Release;
                    LibCfgVmode.Text = string.IsNullOrEmpty(cfg.Vmode) ? "—" : cfg.Vmode;
                    LibCfgCompat.Text = string.IsNullOrEmpty(cfg.Compatibility) ? "—" : cfg.Compatibility;
                    LibCfgDescription.Text = string.IsNullOrEmpty(cfg.Description) ? "—" : cfg.Description;

                    // Use CFG title for UL games with truncated names
                    if (vm.IsUl && !string.IsNullOrEmpty(cfg.Title) && cfg.Title.Length > vm.Name.Length + 2)
                    {
                        TxtLibDetailTitle.Text = cfg.Title;
                        LibDetailName.Text = cfg.Title;
                    }
                }
                else
                {
                    LibCfgInfoPanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                LibCfgInfoPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void LibGamesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LibGamesListBox.SelectedItem is OPLLibraryService.LibraryGame vm)
                OnLibGameSelected(vm);
        }

        private void LibGamesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LibGamesListBox.SelectedItem is OPLLibraryService.LibraryGame vm)
                OnLibGameSelected(vm);
        }

        private void BtnLibDetailBack_Click(object sender, RoutedEventArgs e)
        {
            LibDetailOverlay.Visibility = Visibility.Collapsed;
            LibGridContainer.Visibility = Visibility.Visible;
            LibGamesListBox.SelectedItem = null;
        }

        private void LibScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                LibScreenshotFullImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(path));
                LibScreenshotPopup.Visibility = Visibility.Visible;
            }
        }

        private void LibScreenshotPopup_Click(object sender, MouseButtonEventArgs e)
        {
            LibScreenshotPopup.Visibility = Visibility.Collapsed;
            LibScreenshotFullImage.Source = null;
        }

        private void TxtLibSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtLibSearch.Text == "Buscar juegos...")
            {
                TxtLibSearch.Text = "";
                TxtLibSearch.Foreground = (Brush)FindResource("TextMainBrush");
            }
        }

        private void TxtLibSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtLibSearch.Text))
            {
                TxtLibSearch.Text = "Buscar juegos...";
                TxtLibSearch.Foreground = (Brush)FindResource("TextMutedBrush");
                _libSearchText = "";
            }
            else
            {
                _libSearchText = TxtLibSearch.Text.Trim();
            }
            _libCurrentPage = 1;
            RefreshLibGames();
        }

        private async void TxtLibSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtLibSearch.Text == "Buscar juegos..." || string.IsNullOrWhiteSpace(TxtLibSearch.Text))
            {
                _libSearchCts?.Cancel();
                return;
            }
            _libSearchCts?.Cancel();
            _libSearchCts = new CancellationTokenSource();
            var token = _libSearchCts.Token;
            try
            {
                await Task.Delay(300, token);
                if (!token.IsCancellationRequested)
                {
                    _libSearchText = TxtLibSearch.Text.Trim();
                    _libCurrentPage = 1;
                    RefreshLibGames();
                }
            }
            catch (TaskCanceledException) { }
        }

        private void CboLibSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboLibSort.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _libSortBy = tag;
                _libCurrentPage = 1;
                RefreshLibGames();
            }
        }

        private void BtnLibViewToggle_Checked(object sender, RoutedEventArgs e) => _libIsGridView = true;
        private void BtnLibViewToggle_Unchecked(object sender, RoutedEventArgs e) => _libIsGridView = false;

        private void BtnLibPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_libCurrentPage > 1) { _libCurrentPage--; RefreshLibGames(); }
        }

        private void BtnLibNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_libCurrentPage < _libTotalPages) { _libCurrentPage++; RefreshLibGames(); }
        }

        private async void BtnLibDownloadAllArt_Click(object sender, RoutedEventArgs e)
        {
            if (_libAllGames.Count == 0)
            {
                MessageBox.Show("Primero escanea la librería.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Descargar arte para {_libAllGames.Count} juegos?\nEsto puede tardar mucho tiempo.",
                "Descargar todo el arte", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            var cts = new CancellationTokenSource();
            var progress = new Progress<(string gameId, int current, int total, double percent)>(p =>
            {
                TxtLibScanStatus.Text = $"Descargando arte {p.current}/{p.total} — {p.gameId}";
                double maxWidth = 400;
                LibScanProgressBar.Width = maxWidth * p.percent / 100;
            });

            LibScanProgress.Visibility = Visibility.Visible;
            try
            {
                await OPLLibraryService.DownloadArtForAllAsync(_libAllGames, _libRootPath, progress, cts.Token);
                ToastService.Instance?.ShowSuccess("Arte descargado para todos los juegos");
                await ScanLibraryAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
            finally
            {
                LibScanProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnLibDownloadArt_Click(object sender, RoutedEventArgs e)
        {
            if (_libSelectedGame == null) return;
            try
            {
                string artDir = Path.Combine(_libRootPath, "ART");
                await OPLLibraryService.DownloadArtForGameAsync(_libSelectedGame.GameId, artDir);
                ToastService.Instance?.ShowSuccess($"Arte descargado para {_libSelectedGame.GameId}");
                await ScanLibraryAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
        }

        private async void BtnLibDeleteGame_Click(object sender, RoutedEventArgs e)
        {
            if (_libSelectedGame == null) return;
            var result = MessageBox.Show(
                $"¿Eliminar '{_libSelectedGame.Name}' ({_libSelectedGame.GameId})?\nArchivo: {_libSelectedGame.FilePath}",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (!string.IsNullOrEmpty(_libSelectedGame.FilePath) && File.Exists(_libSelectedGame.FilePath))
                    File.Delete(_libSelectedGame.FilePath);

                // Delete art files
                string artDir = Path.Combine(_libRootPath, "ART");
                if (Directory.Exists(artDir))
                {
                    foreach (var ext in new[] { ".jpg", ".png", ".bmp" })
                    {
                        string covPath = Path.Combine(artDir, $"{_libSelectedGame.GameId}_COV{ext}");
                        if (File.Exists(covPath)) File.Delete(covPath);
                        string icoPath = Path.Combine(artDir, $"{_libSelectedGame.GameId}_ICO{ext}");
                        if (File.Exists(icoPath)) File.Delete(icoPath);
                    }
                }

                ToastService.Instance?.ShowSuccess($"'{_libSelectedGame.Name}' eliminado");
                await ScanLibraryAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
        }

        private void BtnLibEditCfg_Click(object sender, RoutedEventArgs e)
        {
            if (_libSelectedGame == null) return;
            string cfgPath = Path.Combine(_libRootPath, "CFG", $"{_libSelectedGame.GameId}.cfg");

            if (!File.Exists(cfgPath))
            {
                // Create default CFG
                string cfgDir = Path.GetDirectoryName(cfgPath) ?? "";
                if (!Directory.Exists(cfgDir)) Directory.CreateDirectory(cfgDir);
                File.WriteAllText(cfgPath, $"title={_libSelectedGame.Name}\n");
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = cfgPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error abriendo CFG:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnLibDownloadCfg_Click(object sender, RoutedEventArgs e)
        {
            if (_libSelectedGame == null || string.IsNullOrEmpty(_libRootPath)) return;

            BtnLibDownloadCfg.IsEnabled = false;
            BtnLibDownloadCfg.Content = "Descargando...";

            try
            {
                var (success, data, message) = await OPLCfgDatabaseService.DownloadAndSaveCfgAsync(
                    _libRootPath, _libSelectedGame.GameId);

                if (success)
                {
                    ToastService.Instance?.ShowSuccess($"CFG descargado para {_libSelectedGame.GameId}");
                    UpdateCfgButtonAndInfo(_libSelectedGame);
                }
                else
                {
                    ToastService.Instance?.ShowWarning(message);
                }
            }
            catch (Exception ex)
            {
                ToastService.Instance?.ShowError($"Error: {ex.Message}");
            }
            finally
            {
                BtnLibDownloadCfg.Content = "Descargar CFG";
                BtnLibDownloadCfg.IsEnabled = true;
            }
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

        public void Dispose()
        {
            _disposedCts?.Cancel();
            _disposedCts?.Dispose();
            _libSearchCts?.Cancel();
            _libSearchCts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
