using PS2Desktop.Modelos;
using PS2Desktop.Services;
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

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

        public JuegosView() => InitializeComponent();

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            MostrarLoader(true);
            IniciarAnimacionSpinner();

            try
            {
                await CargarJuegos();
            }
            finally
            {
                MostrarLoader(false);
            }
        }

        private void MostrarLoader(bool mostrar)
        {
            LoaderOverlay.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        }

        private void IniciarAnimacionSpinner()
        {
            if (this.Resources["SpinnerAnimation"] is Storyboard sb)
                sb.Begin();
        }

        private async Task CargarJuegos()
        {
            try
            {
                if (AppState.Db == null)
                {
                    AppState.Db = await PostgresService.FromAppSettingsAsync();
                    await AppState.Db.InitializeAsync();
                }

                var juegos = await AppState.Db.GetGamesAsync();
                juegosPanel.Children.Clear();

                if (juegos.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                    return;
                }

                EmptyState.Visibility = Visibility.Collapsed;

                foreach (var juego in juegos)
                {
                    var card = CrearTarjetaJuego(juego);
                    juegosPanel.Children.Add(card);
                }

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
                                if (card.Tag == juego && card.Child is StackPanel stack && stack.Children.Count > 0)
                                {
                                    if (stack.Children[0] is Border imgBorder && imgBorder.Child is Image img)
                                    {
                                        imgBorder.BeginAnimation(Border.OpacityProperty, null);
                                        imgBorder.Opacity = 1;
                                        img.Source = bitmap;
                                        img.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
                                    }
                                    break;
                                }
                            }
                        });
                    }
                    catch { }
                    finally
                    {
                        semaphore.Release();
                    }
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
                Width = 230,
                Margin = new Thickness(0, 0, 20, 30),
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                CornerRadius = new CornerRadius(12),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = juego
            };

            var stackPanel = new StackPanel();

            // Image
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(28, 32, 48)),
                Height = 320,
                ClipToBounds = true
            };

            var image = new Image
            {
                Height = 320,
                Stretch = Stretch.UniformToFill,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            // Skeleton pulse
            image.Opacity = 0;
            var pulseAnim = new DoubleAnimation(0.3, 0.7, TimeSpan.FromSeconds(0.8))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };
            imageBorder.BeginAnimation(Border.OpacityProperty, pulseAnim);

            imageBorder.Child = image;
            stackPanel.Children.Add(imageBorder);

            // Content
            var innerPanel = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };

            var titleBlock = new TextBlock
            {
                Text = juego.nombre,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40
            };
            innerPanel.Children.Add(titleBlock);

            if (!string.IsNullOrEmpty(juego.descripcion))
            {
                var descBlock = new TextBlock
                {
                    Text = juego.descripcion,
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 142, 158)),
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 32,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                innerPanel.Children.Add(descBlock);
            }

            var autorBlock = new TextBlock
            {
                Text = string.IsNullOrEmpty(juego.autor) ? "" : $"Por {juego.autor}",
                Foreground = new SolidColorBrush(Color.FromRgb(136, 142, 158)),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };
            innerPanel.Children.Add(autorBlock);

            // Developer + year badge
            if (!string.IsNullOrEmpty(juego.fecha_lanzamiento) || !string.IsNullOrEmpty(juego.genero))
            {
                var metaPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                if (!string.IsNullOrEmpty(juego.genero))
                {
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    badge.Child = new TextBlock
                    {
                        Text = juego.genero.Split(',')[0].Trim(),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold
                    };
                    metaPanel.Children.Add(badge);
                }
                if (!string.IsNullOrEmpty(juego.fecha_lanzamiento))
                {
                    var yearBlock = new TextBlock
                    {
                        Text = juego.fecha_lanzamiento.Length >= 4
                            ? juego.fecha_lanzamiento.Substring(0, 4) : juego.fecha_lanzamiento,
                        Foreground = new SolidColorBrush(Color.FromRgb(136, 142, 158)),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    metaPanel.Children.Add(yearBlock);
                }
                innerPanel.Children.Add(metaPanel);
            }

            stackPanel.Children.Add(innerPanel);
            border.Child = stackPanel;

            border.MouseEnter += (s, e) =>
            {
                var animBorder = new ThicknessAnimation(
                    new Thickness(0), new Thickness(1.5), TimeSpan.FromSeconds(0.15))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                border.BorderBrush = new SolidColorBrush(Colors.White);
                border.BeginAnimation(Border.BorderThicknessProperty, animBorder);
            };

            border.MouseLeave += (s, e) =>
            {
                var animBorder = new ThicknessAnimation(
                    new Thickness(1.5), new Thickness(0), TimeSpan.FromSeconds(0.2))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                border.BeginAnimation(Border.BorderThicknessProperty, animBorder);
            };

            border.MouseDown += (s, e) => IrADetalle?.Invoke(this, juego);

            return border;
        }
    }
}
