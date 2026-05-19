using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PS2Desktop.Vistas
{
    public partial class TemaView : UserControl
    {
        public event EventHandler<Theme> IrADetalle;

        private IThemeRepository _themeRepo;
        private IFavoriteRepository _favRepo;
        private ISessionService _session;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private static readonly BitmapImage _placeholderImage = new(new Uri("pack://application:,,,/Imagenes/juegosinportada.png"));
        private string? _searchText;
        private string? _sortBy = "date_desc";
        private CancellationTokenSource? _searchCts;
        private List<Border> _tarjetasCreadas = new();

        public TemaView()
        {
            InitializeComponent();
        }

        private IThemeRepository ThemeRepo => _themeRepo ??= App.ServiceProvider.GetRequiredService<IThemeRepository>();
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

        private void ActualizarColoresStackPanel(StackPanel sp, bool isLightMode)
        {
            if (sp == null) return;
            foreach (var child in sp.Children)
            {
                if (child is TextBlock tb)
                {
                    if (tb.FontWeight == FontWeights.Bold)
                        tb.Foreground = new SolidColorBrush(isLightMode ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
                    else
                        tb.Foreground = new SolidColorBrush(isLightMode ? Color.FromRgb(0x88, 0x8E, 0x9E) : Color.FromRgb(0x66, 0x66, 0x66));
                }
                else if (child is StackPanel childSp)
                {
                    ActualizarColoresStackPanel(childSp, isLightMode);
                }
            }
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            MostrarSkeleton(true);
            try { await CargarTemas(); }
            finally { MostrarSkeleton(false); }
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtSearch.Text == "Buscar temas...")
            {
                TxtSearch.Text = "";
                TxtSearch.Foreground = (System.Windows.Media.Brush)FindResource("TextMainBrush");
            }
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                TxtSearch.Text = "Buscar temas...";
                TxtSearch.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
                _searchText = null;
            }
            else
            {
                _searchText = TxtSearch.Text.Trim();
            }
            CardVisualHelper.FireAndForget(() => RecargarTemasAsync(), "Error recargando temas");
        }

        private async void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSearch.Text == "Buscar temas..." || string.IsNullOrWhiteSpace(TxtSearch.Text))
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
                    await RecargarTemasAsync();
                }
            }
            catch (TaskCanceledException) { }
        }

        private void TxtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && TxtSearch.Text != "Buscar temas..." && !string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                e.Handled = true;
                _searchCts?.Cancel();
                _searchText = TxtSearch.Text.Trim();
                CardVisualHelper.FireAndForget(() => RecargarTemasAsync(), "Error recargando temas");
            }
        }

        private async void CboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboSort.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _sortBy = tag;
                await RecargarTemasAsync();
            }
        }

        private async Task RecargarTemasAsync()
        {
            MostrarSkeleton(true);
            try { await CargarTemas(); }
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

        private async System.Threading.Tasks.Task CargarTemas()
        {
            try
            {
                var temas = await ThemeRepo.GetThemesAsync(_searchText, _sortBy);
                temesPanel.Children.Clear();

                if (temas.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(_searchText))
                    {
                        EmptyTitle.Text = "Sin resultados";
                        EmptySubtitle.Text = $"No se encontraron temas para \"{_searchText}\"";
                    }
                    else
                    {
                        EmptyTitle.Text = "No hay temas disponibles";
                        EmptySubtitle.Text = "Los temas aparecerán aquí cuando sean agregados";
                    }
                    EmptyState.Visibility = Visibility.Visible;
                    return;
                }

                EmptyState.Visibility = Visibility.Collapsed;
                foreach (var tema in temas)
                    temesPanel.Children.Add(CrearTarjetaTema(tema));

                CardVisualHelper.FireAndForget(() => CargarImagenesAsync(temas), "Error cargando imágenes");
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError($"Error cargando temas: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task CargarImagenesAsync(List<Theme> temas)
        {
            var maxConcurrent = AppSettings.ImageConcurrency;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<System.Threading.Tasks.Task>();

            foreach (var tema in temas)
            {
                if (string.IsNullOrEmpty(tema.image_url)) continue;
                await semaphore.WaitAsync();
                tasks.Add(System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var bitmap = await _imageCache.GetImageAsync(tema.image_url);
                        if (bitmap == null) { bitmap = _placeholderImage; }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (Border card in temesPanel.Children)
                            {
                                if (card.Tag == tema && CardVisualHelper.TryGetCardImage(card, out _, out var img))
                                {
                                    CardVisualHelper.SetCardImage(card, bitmap, Color.FromRgb(28, 32, 48));
                                    break;
                                }
                            }
                        });
                    }
                    catch (Exception ex) { LoggingService.Instance.Error("Error cargando imagen de tema", ex); }
                    finally { semaphore.Release(); }
                }));
            }
            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        private Border CrearTarjetaTema(Theme tema)
        {
            var border = new Border
            {
                Width = AppSettings.ThemeCardWidth, Margin = new Thickness(0, 0, 20, 30),
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                CornerRadius = new CornerRadius(12), Cursor = System.Windows.Input.Cursors.Hand, Tag = tema
            };
            var stackPanel = new StackPanel();
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x30)),
                Height = AppSettings.ThemeCardHeight, ClipToBounds = true
            };
            var image = new Image
            {
                Height = 140, Stretch = System.Windows.Media.Stretch.UniformToFill,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };
            var scale = new ScaleTransform(1, 1);
            image.RenderTransform = scale;
            image.Opacity = 0;
            imageBorder.BeginAnimation(Border.OpacityProperty,
                new DoubleAnimation(0.3, 0.7, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true });

            imageBorder.MouseEnter += (s, e) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.15, TimeSpan.FromSeconds(0.2)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.15, TimeSpan.FromSeconds(0.2)));
            };
            imageBorder.MouseLeave += (s, e) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.2)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.2)));
            };

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
                    Tag = tema.id
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
                    await FavRepo.ToggleFavoriteAsync(Session.CurrentUser.id, tema.id, "theme");
                    var isFav = await FavRepo.IsFavoriteAsync(Session.CurrentUser.id, tema.id, "theme");
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
                        var isFav = await FavRepo.IsFavoriteAsync(Session.CurrentUser.id, tema.id, "theme");
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
                Text = tema.nombre, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap, MaxHeight = 40
            });

            if (!string.IsNullOrEmpty(tema.descripcion))
            {
                innerPanel.Children.Add(new TextBlock
                {
                    Text = tema.descripcion, Foreground = new SolidColorBrush(Color.FromRgb(136, 142, 158)),
                    FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 32, TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            innerPanel.Children.Add(new TextBlock
            {
                Text = $"Por {tema.autor}", Foreground = new SolidColorBrush(Color.FromRgb(136, 142, 158)),
                FontSize = 11, Margin = new Thickness(0, 8, 0, 0)
            });

            stackPanel.Children.Add(innerPanel);
            border.Child = stackPanel;
            border.MouseDown += (s, e) => IrADetalle?.Invoke(this, tema);
            _tarjetasCreadas.Add(border);
            return border;
        }

    }
}
