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

        public JuegosView()
        {
            InitializeComponent();
            _gameRepo = App.ServiceProvider.GetRequiredService<IGameRepository>();
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
                var juegos = await _gameRepo.GetGamesAsync();
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando juegos: {ex.Message}", "Error");
            }
        }

        private async Task CargarImagenesAsync(List<Game> juegos)
        {
            const int maxConcurrent = 4;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();

            foreach (var juego in juegos)
            {
                var url = juego.image_url;
                if (string.IsNullOrEmpty(url))
                    url = ConstruirCoverUrl(juego.game_id);
                if (string.IsNullOrEmpty(url)) continue;

                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await _httpClient.GetByteArrayAsync(url);
                        var bitmap = await Dispatcher.InvokeAsync(() =>
                        {
                            using var ms = new MemoryStream(bytes);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            return bmp;
                        });

                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (Border card in juegosPanel.Children)
                            {
                                if (card.Tag == juego && card.Child is StackPanel stack && stack.Children.Count > 0
                                    && stack.Children[0] is Border imgBorder && imgBorder.Child is Image img)
                                {
                                    imgBorder.BeginAnimation(Border.OpacityProperty, null);
                                    imgBorder.Opacity = 1;
                                    img.Source = bitmap;
                                    img.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
                                    break;
                                }
                            }
                        });
                    }
                    catch { }
                    finally { semaphore.Release(); }
                }));
            }
            await Task.WhenAll(tasks);
        }

        private static string? ConstruirCoverUrl(string? gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;
            return $"https://raw.githubusercontent.com/Luden02/psx-ps2-opl-art-database/main/PS2/{gameId}/{gameId}_COV.png";
        }

        private Border CrearTarjetaJuego(Game juego)
        {
            var border = new Border
            {
                Width = 230, Margin = new Thickness(0, 0, 20, 30),
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                CornerRadius = new CornerRadius(12), Cursor = System.Windows.Input.Cursors.Hand, Tag = juego
            };

            var stackPanel = new StackPanel();
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(28, 32, 48)),
                Height = 320, ClipToBounds = true
            };
            var image = new Image
            {
                Height = 320, Stretch = Stretch.UniformToFill,
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

            var defaultBg = Color.FromRgb(18, 18, 18);
            var hoverBg = Color.FromRgb(30, 30, 35);

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
            return border;
        }
    }
}
