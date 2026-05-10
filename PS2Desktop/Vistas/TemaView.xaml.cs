using PS2Desktop.Modelos;
using PS2Desktop.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PS2Desktop.Vistas
{
    /// <summary>
    /// L�gica de interacci�n para TemaView.xaml
    /// </summary>
    public partial class TemaView : UserControl
    {
        public event EventHandler<Theme> IrADetalle;

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public TemaView() => InitializeComponent();

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Mostrar loader
            MostrarLoader(true);

            // Iniciar la animaci�n del spinner
            IniciarAnimacionSpinner();

            try
            {
                await CargarTemas();
            }
            finally
            {
                // Ocultar loader
                MostrarLoader(false);
            }
        }

        private void MostrarLoader(bool mostrar)
        {
            LoaderOverlay.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        }

        private void IniciarAnimacionSpinner()
        {
            var storyboard = (Storyboard)this.Resources["SpinnerAnimation"];
            storyboard.Begin();
        }

        private async System.Threading.Tasks.Task CargarTemas()
        {
            try
            {
                if (AppState.Db == null)
                {
                    AppState.Db = await PostgresService.FromAppSettingsAsync();
                    await AppState.Db.InitializeAsync();
                }

                var temas = await AppState.Db.GetThemesAsync();

                // Limpiar items existentes
                temesPanel.Children.Clear();

                if (temas.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                    return;
                }

                EmptyState.Visibility = Visibility.Collapsed;

                // Crear tarjetas sin imágenes primero (UI responsive)
                foreach (var tema in temas)
                {
                    var card = CrearTarjetaTema(tema);
                    temesPanel.Children.Add(card);
                }

                // Cargar imágenes en segundo plano con concurrencia limitada
                _ = CargarImagenesAsync(temas);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando temas: {ex.Message}", "Error");
            }
        }

        private async Task CargarImagenesAsync(List<Theme> temas)
        {
            const int maxConcurrent = 4;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();

            foreach (var tema in temas)
            {
                if (string.IsNullOrEmpty(tema.image_url)) continue;

                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
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
                                if (card.Tag == tema && card.Child is StackPanel stack && stack.Children.Count > 0)
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
                    catch { /* ignore failed images */ }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            await Task.WhenAll(tasks);
        }

        private Border CrearTarjetaTema(Theme tema)
        {
            var border = new Border
            {
                Width = 240,
                Margin = new Thickness(0, 0, 20, 30),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 18)),
                CornerRadius = new CornerRadius(12),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = tema
            };

            var stackPanel = new StackPanel();

            // Imagen del tema
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 32, 48)),
                Height = 140,
                ClipToBounds = true
            };

            var image = new Image
            {
                Height = 140,
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };

            var scale = new System.Windows.Media.ScaleTransform(1, 1);
            image.RenderTransform = scale;

            // Skeleton pulse mientras se carga la imagen
            image.Opacity = 0;
            var pulseAnim = new DoubleAnimation(0.3, 0.7, TimeSpan.FromSeconds(0.8))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };
            imageBorder.BeginAnimation(Border.OpacityProperty, pulseAnim);

            imageBorder.MouseEnter += (s, e) =>
            {
                var animX = new DoubleAnimation(1.15, TimeSpan.FromSeconds(0.2));
                var animY = new DoubleAnimation(1.15, TimeSpan.FromSeconds(0.2));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animX);
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, animY);
            };

            imageBorder.MouseLeave += (s, e) =>
            {
                var animX = new DoubleAnimation(1, TimeSpan.FromSeconds(0.2));
                var animY = new DoubleAnimation(1, TimeSpan.FromSeconds(0.2));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animX);
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, animY);
            };

            imageBorder.Child = image;
            stackPanel.Children.Add(imageBorder);

            // Contenido interno
            var innerPanel = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };

            // T�tulo del tema
            var titleBlock = new TextBlock
            {
                Text = tema.nombre,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 15,
                FontWeight = System.Windows.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40
            };
            innerPanel.Children.Add(titleBlock);

            // Descripci�n
            if (!string.IsNullOrEmpty(tema.descripcion))
            {
                var descBlock = new TextBlock
                {
                    Text = tema.descripcion,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 142, 158)),
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 32,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                innerPanel.Children.Add(descBlock);
            }

            // Autor
            var autorBlock = new TextBlock
            {
                Text = $"Por {tema.autor}",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 142, 158)),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };
            innerPanel.Children.Add(autorBlock);

            stackPanel.Children.Add(innerPanel);
            border.Child = stackPanel;

            border.MouseDown += (s, e) => IrADetalle?.Invoke(this, tema);

            return border;
        }

        private void btnTemaDetalle_Click(object sender, RoutedEventArgs e)
        {
            // Este evento ya no se usa, pero lo mantenemos por compatibilidad
            IrADetalle?.Invoke(this, null);
        }
    }
}
