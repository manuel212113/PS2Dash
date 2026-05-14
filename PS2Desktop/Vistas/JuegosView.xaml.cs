using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

        private readonly IGameRepository _gameRepo;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _totalGames = 0;
        private const int GamesPerPage = 20;
        private List<Border> _tarjetasCreadas = new();

        public JuegosView()
        {
            InitializeComponent();
            _gameRepo = App.ServiceProvider.GetRequiredService<IGameRepository>();
            App.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) => App.ThemeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(bool isLightMode)
        {
            ActualizarColoresTarjetas(isLightMode);
        }

        private void ActualizarColoresTarjetas(bool isLightMode)
        {
            var bgDark = Color.FromRgb(0x12, 0x12, 0x12);
            var bgLight = Color.FromRgb(0xFF, 0xFF, 0xFF);
            var cardBgDark = Color.FromRgb(0x20, 0x20, 0x20);
            var cardBgLight = Color.FromRgb(0xF0, 0xF0, 0xF0);
            var imgBgDark = Color.FromRgb(0x1C, 0x20, 0x30);
            var imgBgLight = Color.FromRgb(0xE8, 0xE8, 0xE8);
            var textDark = Colors.White;
            var textLight = Color.FromRgb(0x1A, 0x1A, 0x1A);
            var textSecDark = Color.FromRgb(0x88, 0x8E, 0x9E);
            var textSecLight = Color.FromRgb(0x66, 0x66, 0x66);

            foreach (var tarjeta in _tarjetasCreadas)
            {
                if (tarjeta.Background is SolidColorBrush brush)
                {
                    brush.Color = isLightMode ? cardBgDark : cardBgLight;
                }
                if (tarjeta.Child is StackPanel mainSp)
                {
                    if (mainSp.Children.Count > 0 && mainSp.Children[0] is Border imgBorder)
                    {
                        if (imgBorder.Background is SolidColorBrush imgBrush)
                        {
                            imgBrush.Color = isLightMode ? imgBgDark : imgBgLight;
                        }
                    }
                    ActualizarColoresStackPanel(mainSp, isLightMode, textDark, textLight, textSecDark, textSecLight);
                }
            }
        }

        private void ActualizarColoresStackPanel(StackPanel sp, bool isLightMode, Color textDark, Color textLight, Color textSecDark, Color textSecLight)
        {
            if (sp == null) return;
            foreach (var child in sp.Children)
            {
                if (child is TextBlock tb)
                {
                    if (tb.FontWeight == FontWeights.Bold)
                        tb.Foreground = new SolidColorBrush(isLightMode ? textDark : textLight);
                    else
                        tb.Foreground = new SolidColorBrush(isLightMode ? textSecDark : textSecLight);
                }
                else if (child is StackPanel childSp)
                {
                    ActualizarColoresStackPanel(childSp, isLightMode, textDark, textLight, textSecDark, textSecLight);
                }
                else if (child is Border b && b.Child is TextBlock badgeText)
                {
                    badgeText.Foreground = new SolidColorBrush(Colors.White);
                }
            }
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            MostrarLoader(true);
            IniciarAnimacionSpinner();
            try { await CargarJuegos(); }
            finally { MostrarLoader(false); }
        }

        private void MostrarLoader(bool mostrar) =>
            LoaderOverlay.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;

        private void IniciarAnimacionSpinner()
        {
            if (this.Resources["SpinnerAnimation"] is Storyboard sb)
                sb.Begin();
        }

        private async Task CargarJuegos()
        {
            try
            {
                _totalGames = await _gameRepo.GetGameCountAsync();
                _totalPages = Math.Max(1, (int)Math.Ceiling((double)_totalGames / GamesPerPage));
                if (_currentPage > _totalPages) _currentPage = _totalPages;
                if (_currentPage < 1) _currentPage = 1;
                
                var offset = (_currentPage - 1) * GamesPerPage;
                var juegos = await _gameRepo.GetGamesAsync(GamesPerPage, offset);
                juegosPanel.Children.Clear();

                if (juegos.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                    return;
                }

                EmptyState.Visibility = Visibility.Collapsed;
                foreach (var juego in juegos)
                    juegosPanel.Children.Add(CrearTarjetaJuego(juego));

                _ = CargarImagenesAsync(juegos);
                
                TxtPageInfo.Text = _currentPage.ToString();
                TxtTotalPages.Text = _totalPages.ToString();
                BtnPrevPage.IsEnabled = _currentPage > 1;
                BtnNextPage.IsEnabled = _currentPage < _totalPages;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando juegos: {ex.Message}", "Error");
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
            const int maxConcurrent = 4;
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

                        // Try 3D first
                        if (!string.IsNullOrEmpty(url3d))
                        {
                            try
                            {
                                var bytes3d = await _httpClient.GetByteArrayAsync(url3d);
                                bitmap3d = await Dispatcher.InvokeAsync(() =>
                                {
                                    using var ms = new MemoryStream(bytes3d);
                                    var bmp = new BitmapImage();
                                    bmp.BeginInit();
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.StreamSource = ms;
                                    bmp.EndInit();
                                    bmp.Freeze();
                                    return bmp;
                                });
                            }
                            catch { }
                        }

                        // Try 2D second
                        if (!string.IsNullOrEmpty(url2d))
                        {
                            try
                            {
                                var bytes2d = await _httpClient.GetByteArrayAsync(url2d);
                                bitmap2d = await Dispatcher.InvokeAsync(() =>
                                {
                                    using var ms = new MemoryStream(bytes2d);
                                    var bmp = new BitmapImage();
                                    bmp.BeginInit();
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.StreamSource = ms;
                                    bmp.EndInit();
                                    bmp.Freeze();
                                    return bmp;
                                });
                            }
                            catch { }
                        }

                        // Try fallback image_url third
                        if (!string.IsNullOrEmpty(urlFallback))
                        {
                            try
                            {
                                var bytesFallback = await _httpClient.GetByteArrayAsync(urlFallback);
                                bitmapFallback = await Dispatcher.InvokeAsync(() =>
                                {
                                    using var ms = new MemoryStream(bytesFallback);
                                    var bmp = new BitmapImage();
                                    bmp.BeginInit();
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.StreamSource = ms;
                                    bmp.EndInit();
                                    bmp.Freeze();
                                    return bmp;
                                });
                            }
                            catch { }
                        }

                        // Determine primary (displayed) and secondary (on hover)
                        var primary = bitmap3d ?? bitmap2d ?? bitmapFallback;
                        var isPrimary3d = bitmap3d != null;
                        var secondary = (bitmap3d != null && bitmap2d != null) ? bitmap2d : 
                                        (bitmap3d != null && bitmapFallback != null) ? bitmapFallback : 
                                        (bitmap2d != null && bitmapFallback != null) ? bitmapFallback : null;

                        if (primary == null) { semaphore.Release(); return; }

                        var backgroundColor = isPrimary3d ? Colors.Transparent : Color.FromRgb(28, 32, 48);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (Border card in juegosPanel.Children)
                            {
                                if (card.Tag == juego && card.Child is StackPanel stack && stack.Children.Count > 0
                                    && stack.Children[0] is Border imgBorder && imgBorder.Child is Image img)
                                {
                                    imgBorder.BeginAnimation(Border.OpacityProperty, null);
                                    imgBorder.Opacity = 1;
                                    imgBorder.Background = new SolidColorBrush(backgroundColor);
                                    img.Source = primary;
                                    img.Tag = secondary; // Store the hover image
                                    img.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));

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
                    finally { semaphore.Release(); }
                }));
            }
            await Task.WhenAll(tasks);
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
            var isLight = AppSettings.IsLightMode;
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = new SolidColorBrush(isLight ? Color.FromRgb(0xE8, 0xE8, 0xE8) : Color.FromRgb(0x1C, 0x20, 0x30)),
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
            stackPanel.Children.Add(imageBorder);

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

            var defaultBg = isLight ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x12, 0x12, 0x12);
            var hoverBg = isLight ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x2A, 0x2A, 0x2A);
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
