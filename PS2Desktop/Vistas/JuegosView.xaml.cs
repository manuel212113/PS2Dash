using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PS2Desktop.Vistas
{
    public partial class JuegosView : UserControl
    {
        public event EventHandler<Game> IrADetalle;

        private IGameRepository _gameRepo;
        private IFavoriteRepository _favRepo;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private static readonly BitmapImage _placeholderImage = new(new Uri("pack://application:,,,/Imagenes/juegosinportada.png"));
        private ISessionService _session;

        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _totalGames = 0;
        private int GamesPerPage => AppSettings.PageSize;
        private string? _searchText;
        private string? _sortBy = "date_desc";
        private string? _genreFilter;
        private CancellationTokenSource? _searchCts;
        private List<Border> _tarjetasCreadas = new();

        public JuegosView()
        {
            InitializeComponent();
        }

        private IGameRepository GameRepo => _gameRepo ??= App.ServiceProvider.GetRequiredService<IGameRepository>();
        private IFavoriteRepository FavRepo => _favRepo ??= App.ServiceProvider.GetRequiredService<IFavoriteRepository>();
        private ISessionService Session => _session ??= App.ServiceProvider.GetRequiredService<ISessionService>();

        public void FocusSearch()
        {
            TxtSearch.Focus();
            TxtSearch.SelectAll();
        }

        private void ActualizarColoresTarjetas()
        {
            foreach (var tarjeta in _tarjetasCreadas)
            {
                if (tarjeta.Child is StackPanel mainSp)
                {
                    if (mainSp.Children.Count > 0 && mainSp.Children[0] is Border imgBorder)
                    {
                        if (imgBorder.Background is SolidColorBrush imgBrush)
                            imgBrush.Color = Color.FromRgb(0x1C, 0x20, 0x30);
                    }
                    ActualizarColoresStackPanel(mainSp);
                }
            }
        }

        private void ActualizarColoresStackPanel(StackPanel sp)
        {
            if (sp == null) return;
            foreach (var child in sp.Children)
            {
                if (child is TextBlock tb)
                {
                    tb.Foreground = new SolidColorBrush(tb.FontWeight == FontWeights.Bold ? Colors.White : Color.FromRgb(0x88, 0x8E, 0x9E));
                }
                else if (child is StackPanel childSp)
                {
                    ActualizarColoresStackPanel(childSp);
                }
            }
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            MostrarSkeleton(true);
            try { await CargarJuegos(); }
            finally { MostrarSkeleton(false); }
        }

        private void MostrarLoader(bool mostrar) =>
            LoaderOverlay.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;

        private void MostrarSkeleton(bool mostrar)
        {
            if (skeletonPanel == null) return;
            skeletonPanel.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
            if (mostrar)
            {
                foreach (var child in skeletonPanel.Children)
                {
                    if (child is Border skeleton && Resources["ShimmerAnimation"] is Storyboard sb)
                        sb.Begin(skeleton);
                }
            }
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtSearch.Text == "Buscar juegos...")
            {
                TxtSearch.Text = "";
                TxtSearch.Foreground = (System.Windows.Media.Brush)FindResource("TextMainBrush");
            }
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                TxtSearch.Text = "Buscar juegos...";
                TxtSearch.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
                _searchText = null;
            }
            else
            {
                _searchText = TxtSearch.Text.Trim();
            }
            _currentPage = 1;
            CardVisualHelper.FireAndForget(() => RecargarJuegosAsync(), "Error recargando juegos");
        }

        private async void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSearch.Text == "Buscar juegos..." || string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                _searchCts?.Cancel();
                return;
            }
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            try
            {
                await Task.Delay(400, token);
                if (!token.IsCancellationRequested)
                {
                    _searchText = TxtSearch.Text.Trim();
                    _currentPage = 1;
                    await RecargarJuegosAsync();
                }
            }
            catch (TaskCanceledException) { }
        }

        private void TxtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && TxtSearch.Text != "Buscar juegos..." && !string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                e.Handled = true;
                _searchCts?.Cancel();
                _searchText = TxtSearch.Text.Trim();
                _currentPage = 1;
                CardVisualHelper.FireAndForget(() => RecargarJuegosAsync(), "Error recargando juegos");
            }
        }

        private async void CboGenre_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboGenre.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _genreFilter = string.IsNullOrEmpty(tag) ? null : tag;
                _currentPage = 1;
                await RecargarJuegosAsync();
            }
        }

        private async void CboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboSort.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _sortBy = tag;
                _currentPage = 1;
                await RecargarJuegosAsync();
            }
        }

        private async Task RecargarJuegosAsync()
        {
            MostrarSkeleton(true);
            try { await CargarJuegos(); }
            finally { MostrarSkeleton(false); }
        }

        private async Task CargarJuegos()
        {
            try
            {
                _totalGames = await GameRepo.GetGameCountAsync(_searchText, _genreFilter);
                _totalPages = Math.Max(1, (int)Math.Ceiling((double)_totalGames / GamesPerPage));
                if (_currentPage > _totalPages) _currentPage = _totalPages;
                if (_currentPage < 1) _currentPage = 1;
                
                var offset = (_currentPage - 1) * GamesPerPage;
                var juegos = await GameRepo.GetGamesAsync(GamesPerPage, offset, _searchText, _sortBy, _genreFilter);
                juegosPanel.Children.Clear();

                if (juegos.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(_searchText))
                    {
                        EmptyTitle.Text = "Sin resultados";
                        EmptySubtitle.Text = $"No se encontraron juegos para \"{_searchText}\"";
                    }
                    else if (!string.IsNullOrWhiteSpace(_genreFilter))
                    {
                        EmptyTitle.Text = "Sin resultados";
                        EmptySubtitle.Text = $"No hay juegos en la categoría {_genreFilter}";
                    }
                    else
                    {
                        EmptyTitle.Text = "No hay juegos disponibles";
                        EmptySubtitle.Text = "Los juegos aparecerán aquí cuando sean agregados";
                    }
                    EmptyState.Visibility = Visibility.Visible;
                    return;
                }

                EmptyState.Visibility = Visibility.Collapsed;
                foreach (var juego in juegos)
                    juegosPanel.Children.Add(CrearTarjetaJuego(juego));

                await CargarImagenesAsync(juegos);

                TxtPageInfo.Text = _currentPage.ToString();
                TxtTotalPages.Text = _totalPages.ToString();
                BtnPrevPage.IsEnabled = _currentPage > 1;
                BtnNextPage.IsEnabled = _currentPage < _totalPages;
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError($"Error cargando juegos: {ex.Message}");
            }
        }

        private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                MostrarLoader(true);
                try { await CargarJuegos(); }
                finally { MostrarLoader(false); }
            }
        }

        private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                MostrarLoader(true);
                try { await CargarJuegos(); }
                finally { MostrarLoader(false); }
            }
        }

        private async Task CargarImagenesAsync(List<Game> juegos)
        {
            var maxConcurrent = AppSettings.ImageConcurrency;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();

            foreach (var juego in juegos)
            {
                var url3d = ConstruirCoverUrl3d(juego.game_id);
                var url2d = ConstruirCoverUrl(juego.game_id);
                var urlFallback = juego.image_url;

                if (string.IsNullOrEmpty(url3d) && string.IsNullOrEmpty(url2d) && string.IsNullOrEmpty(urlFallback)) continue;

                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        BitmapImage? bitmap3d = null;
                        BitmapImage? bitmap2d = null;
                        BitmapImage? bitmapFallback = null;

                        if (!string.IsNullOrEmpty(url3d))
                            bitmap3d = await _imageCache.GetImageAsync(url3d);

                        if (!string.IsNullOrEmpty(url2d))
                            bitmap2d = await _imageCache.GetImageAsync(url2d);

                        if (!string.IsNullOrEmpty(urlFallback))
                            bitmapFallback = await _imageCache.GetImageAsync(urlFallback);

                        var primary = bitmap3d ?? bitmap2d ?? bitmapFallback;
                        var isPrimary3d = bitmap3d != null;
                        var secondary = (bitmap3d != null && bitmap2d != null) ? bitmap2d : 
                                        (bitmap3d != null && bitmapFallback != null) ? bitmapFallback : 
                                        (bitmap2d != null && bitmapFallback != null) ? bitmapFallback : null;

                        if (primary == null) { primary = _placeholderImage; isPrimary3d = false; }

                        var backgroundColor = isPrimary3d ? Colors.Transparent : Color.FromRgb(28, 32, 48);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (Border card in juegosPanel.Children)
                            {
                                if (card.Tag == juego && CardVisualHelper.TryGetCardImage(card, out var imgBorder, out var img))
                                {
                                    CardVisualHelper.SetCardImage(card, primary, backgroundColor);
                                    img.Tag = secondary;

                                    if (secondary != null)
                                    {
                                        var isSecondary3d = bitmap3d != null && secondary == bitmap2d || bitmap3d != null && secondary == bitmapFallback;
                                        var secondaryBackground = isSecondary3d ? new SolidColorBrush(Colors.Transparent) : new SolidColorBrush(Color.FromRgb(28, 32, 48));

                                        card.MouseEnter += (s, e) =>
                                        {
                                            img.Source = secondary;
                                            imgBorder.Background = secondaryBackground;
                                            img.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, 0.8, TimeSpan.FromMilliseconds(200)));
                                        };
                                        card.MouseLeave += (s, e) =>
                                        {
                                            img.Source = primary;
                                            imgBorder.Background = new SolidColorBrush(backgroundColor);
                                            img.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(200)));
                                        };
                                    }
                                    break;
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() =>
                            ToastService.Instance.ShowError($"Error cargando imagen: {ex.Message}")
                        );
                    }
                    finally { semaphore.Release(); }
                }));
            }
            try { await Task.WhenAll(tasks); }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    ToastService.Instance.ShowError($"Error en carga de imágenes: {ex.Message}")
                );
            }
        }

        private static string TransformarGameId(string? gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return "";
            return gameId.Replace("_", "-").Replace(".", "");
        }

        private static string ConstruirCoverUrl(string? gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return "";
            var id = TransformarGameId(gameId);
            return $"https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/{id}.jpg";
        }

        private static string ConstruirCoverUrl3d(string? gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return "";
            var id = TransformarGameId(gameId);
            return $"https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/3d/{id}.png";
        }

        private Border CrearTarjetaJuego(Game juego)
        {
            var cardWidth = AppSettings.GameCardWidth;
            var cardHeight = AppSettings.GameCardHeight;
            
            var border = new Border
            {
                Width = cardWidth, Margin = new Thickness(0, 0, 20, 30),
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                CornerRadius = new CornerRadius(12), Cursor = System.Windows.Input.Cursors.Hand, Tag = juego
            };

            var stackPanel = new StackPanel();
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x30)),
                Height = cardHeight, ClipToBounds = true
            };
            var image = new Image
            {
                Height = cardHeight, Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            image.Opacity = 0;
            imageBorder.BeginAnimation(Border.OpacityProperty,
                new DoubleAnimation(0.3, 0.7, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true });
            imageBorder.Child = image;

            var imageContainer = new Grid();
            imageContainer.Children.Add(imageBorder);

            if (Session.IsLoggedIn)
            {
                var favBtn = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 8, 8, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = juego.id
                };
                var favIcon = new TextBlock
                {
                    Text = "♡",
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                favBtn.Child = favIcon;
                favBtn.MouseDown += async (s, e) =>
                {
                    e.Handled = true;
                    if (Session.CurrentUser == null) return;
                    await FavRepo.ToggleFavoriteAsync(Session.CurrentUser.id, juego.id, "game");
                    var isFav = await FavRepo.IsFavoriteAsync(Session.CurrentUser.id, juego.id, "game");
                    favIcon.Text = isFav ? "♥" : "♡";
                    favIcon.Foreground = isFav
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x45))
                        : new SolidColorBrush(Colors.White);
                };
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (Session.CurrentUser == null) return;
                        var isFav = await FavRepo.IsFavoriteAsync(Session.CurrentUser.id, juego.id, "game");
                        await Dispatcher.InvokeAsync(() =>
                        {
                            favIcon.Text = isFav ? "♥" : "♡";
                            favIcon.Foreground = isFav
                                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x45))
                                : new SolidColorBrush(Colors.White);
                        });
                    }
                    catch (Exception ex) { LoggingService.Instance.Error("Error checking favorite status", ex); }
                });
                imageContainer.Children.Add(favBtn);
            }

            stackPanel.Children.Add(imageContainer);

            var innerPanel = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
            innerPanel.Children.Add(new TextBlock
            {
                Text = juego.nombre, Foreground = new SolidColorBrush(Colors.White),
                FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxHeight = 44
            });

            if (!string.IsNullOrEmpty(juego.descripcion))
            {
                innerPanel.Children.Add(new TextBlock
                {
                    Text = juego.descripcion, Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 180)),
                    FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            innerPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(juego.autor) ? "" : $"Por {juego.autor}",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 180)), FontSize = 11, Margin = new Thickness(0, 8, 0, 0)
            });

            if (!string.IsNullOrEmpty(juego.fecha_lanzamiento) || !string.IsNullOrEmpty(juego.genero))
            {
                var metaPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                if (!string.IsNullOrEmpty(juego.genero))
                {
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC)),
                        CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 6, 0)
                    };
                    badge.Child = new TextBlock
                    {
                        Text = juego.genero.Split(',')[0].Trim(), Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 10, FontWeight = FontWeights.SemiBold
                    };
                    metaPanel.Children.Add(badge);
                }
                if (!string.IsNullOrEmpty(juego.fecha_lanzamiento))
                {
                    metaPanel.Children.Add(new TextBlock
                    {
                        Text = juego.fecha_lanzamiento.Length >= 4 ? juego.fecha_lanzamiento.Substring(0, 4) : juego.fecha_lanzamiento,
                        Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 180)), FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                innerPanel.Children.Add(metaPanel);
            }

            stackPanel.Children.Add(innerPanel);
            border.Child = stackPanel;

            var defaultBg = Color.FromRgb(0x12, 0x12, 0x12);
            var hoverBg = Color.FromRgb(0x2A, 0x2A, 0x2A);
            border.Background = new SolidColorBrush(defaultBg);

            border.MouseEnter += (s, e) =>
            {
                var animBg = new ColorAnimation(defaultBg, hoverBg, TimeSpan.FromSeconds(0.15))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                border.Background = new SolidColorBrush(hoverBg);
                border.Background.BeginAnimation(SolidColorBrush.ColorProperty, animBg);
            };

            border.MouseLeave += (s, e) =>
            {
                var animBg = new ColorAnimation(hoverBg, defaultBg, TimeSpan.FromSeconds(0.2))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                border.Background = new SolidColorBrush(defaultBg);
                border.Background.BeginAnimation(SolidColorBrush.ColorProperty, animBg);
            };

            border.MouseDown += (s, e) => IrADetalle?.Invoke(this, juego);

            _tarjetasCreadas.Add(border);
            return border;
        }
    }
}
