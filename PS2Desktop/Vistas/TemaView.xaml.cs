using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

        private readonly IThemeRepository _themeRepo;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public TemaView()
        {
            InitializeComponent();
            _themeRepo = App.ServiceProvider.GetRequiredService<IThemeRepository>();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            MostrarLoader(true);
            IniciarAnimacionSpinner();
            try { await CargarTemas(); }
            finally { MostrarLoader(false); }
        }

        private void MostrarLoader(bool mostrar) =>
            LoaderOverlay.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;

        private void IniciarAnimacionSpinner() =>
            ((Storyboard)this.Resources["SpinnerAnimation"]).Begin();

        private async System.Threading.Tasks.Task CargarTemas()
        {
            try
            {
                var temas = await _themeRepo.GetThemesAsync();
                temesPanel.Children.Clear();

                if (temas.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                    return;
                }

                EmptyState.Visibility = Visibility.Collapsed;
                foreach (var tema in temas)
                    temesPanel.Children.Add(CrearTarjetaTema(tema));

                _ = CargarImagenesAsync(temas);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando temas: {ex.Message}", "Error");
            }
        }

        private async System.Threading.Tasks.Task CargarImagenesAsync(List<Theme> temas)
        {
            const int maxConcurrent = 4;
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
                        var bytes = await _httpClient.GetByteArrayAsync(tema.image_url);
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
                            foreach (Border card in temesPanel.Children)
                            {
                                if (card.Tag == tema && card.Child is StackPanel stack && stack.Children.Count > 0
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
            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        private Border CrearTarjetaTema(Theme tema)
        {
            var border = new Border
            {
                Width = 240, Margin = new Thickness(0, 0, 20, 30),
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                CornerRadius = new CornerRadius(12), Cursor = System.Windows.Input.Cursors.Hand, Tag = tema
            };
            var stackPanel = new StackPanel();
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(28, 32, 48)),
                Height = 140, ClipToBounds = true
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
            stackPanel.Children.Add(imageBorder);

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
            return border;
        }

        private void btnTemaDetalle_Click(object sender, RoutedEventArgs e) =>
            IrADetalle?.Invoke(this, null);
    }
}
