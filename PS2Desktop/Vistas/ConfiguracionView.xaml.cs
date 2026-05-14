using PS2Desktop.Services;
using System.Windows;
using System.Windows.Controls;

namespace PS2Desktop.Vistas
{
    public partial class ConfiguracionView : UserControl
    {
        public ConfiguracionView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                CargarConfiguracion();
                ActualizarPreview();
            };

            SliderGameWidth.ValueChanged += (s, e) => TxtGameWidth.Text = $"{SliderGameWidth.Value} px";
            SliderGameHeight.ValueChanged += (s, e) => TxtGameHeight.Text = $"{SliderGameHeight.Value} px";
            SliderThemeWidth.ValueChanged += (s, e) => TxtThemeWidth.Text = $"{SliderThemeWidth.Value} px";
            SliderThemeHeight.ValueChanged += (s, e) => TxtThemeHeight.Text = $"{SliderThemeHeight.Value} px";
        }

        private async void CargarConfiguracion()
        {
            await AppSettings.LoadAsync();
            SliderGameWidth.Value = AppSettings.GameCardWidth;
            SliderGameHeight.Value = AppSettings.GameCardHeight;
            SliderThemeWidth.Value = AppSettings.ThemeCardWidth;
            SliderThemeHeight.Value = AppSettings.ThemeCardHeight;
            ActualizarPreview();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ActualizarPreview();
        }

        private void ActualizarPreview()
        {
            if (PreviewCard == null || TxtGameWidth == null || TxtGameHeight == null ||
                TxtWidthPreview == null || TxtHeightPreview == null) return;

            PreviewCard.Width = SliderGameWidth.Value;
            PreviewCard.Height = SliderGameHeight.Value;
            PreviewCard.UpdateLayout();

            TxtGameWidth.Text = $"{(int)SliderGameWidth.Value} px";
            TxtGameHeight.Text = $"{(int)SliderGameHeight.Value} px";
            TxtWidthPreview.Text = $"{(int)SliderGameWidth.Value} px";
            TxtHeightPreview.Text = $"{(int)SliderGameHeight.Value} px";
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            await AppSettings.SaveAsync(
                SliderGameWidth.Value,
                SliderGameHeight.Value,
                SliderThemeWidth.Value,
                SliderThemeHeight.Value,
                AppSettings.IsLightMode
            );
            MessageBox.Show("Configuración guardada.", "Guardado");
        }
    }
}