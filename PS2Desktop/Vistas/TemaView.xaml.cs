using PS2Desktop.Modelos;
using PS2Desktop.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PS2Desktop.Vistas
{
    /// <summary>
    /// Lógica de interacción para TemaView.xaml
    /// </summary>
    public partial class TemaView : UserControl
    {
        public event EventHandler<Theme> IrADetalle;

        public TemaView() => InitializeComponent();

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Mostrar loader
            MostrarLoader(true);

            // Iniciar la animación del spinner
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

                // Agregar cada tema como una tarjeta
                foreach (var tema in temas)
                {
                    var card = CrearTarjetaTema(tema);
                    temesPanel.Children.Add(card);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando temas: {ex.Message}", "Error");
            }
        }

        private Border CrearTarjetaTema(Theme tema)
        {
            var border = new Border
            {
                Width = 310,
                Margin = new Thickness(0, 0, 20, 40),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = tema  // Almacenar el tema en el Tag
            };

            var stackPanel = new StackPanel();

            // Imagen del tema
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 32, 48)),
                Margin = new Thickness(0, 0, 0, 12),
                Height = 175,
                ClipToBounds = true
            };

            var image = new Image
            {
                Height = 175,
                Stretch = System.Windows.Media.Stretch.UniformToFill
            };

            try
            {
                if (!string.IsNullOrEmpty(tema.image_url))
                {
                    image.Source = new BitmapImage(new Uri(tema.image_url, UriKind.Absolute));
                }
            }
            catch { }

            imageBorder.Child = image;
            stackPanel.Children.Add(imageBorder);

            // Título del tema
            var titleBlock = new TextBlock
            {
                Text = tema.nombre,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 18,
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new Thickness(5, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(titleBlock);

            // Descripción
            if (!string.IsNullOrEmpty(tema.descripcion))
            {
                var descBlock = new TextBlock
                {
                    Text = tema.descripcion,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 142, 158)),
                    FontSize = 12,
                    Margin = new Thickness(5, 8, 5, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 40,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                stackPanel.Children.Add(descBlock);
            }

            // Información (autor)
            var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 10, 0, 0) };

            var autorBlock = new TextBlock
            {
                Text = $"Por {tema.autor}",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 142, 158)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            infoPanel.Children.Add(autorBlock);
            stackPanel.Children.Add(infoPanel);

            border.Child = stackPanel;

            // Agregar evento click a la tarjeta
            border.MouseDown += (sender, e) =>
            {
                if (border.Tag is Theme temaSeleccionado)
                {
                    IrADetalle?.Invoke(this, temaSeleccionado);
                }
            };

            return border;
        }

        private void btnTemaDetalle_Click(object sender, RoutedEventArgs e)
        {
            // Este evento ya no se usa, pero lo mantenemos por compatibilidad
            IrADetalle?.Invoke(this, null);
        }
    }
}
