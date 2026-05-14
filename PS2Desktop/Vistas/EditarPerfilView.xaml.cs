using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PS2Desktop.Vistas
{
    public partial class EditarPerfilView : UserControl
    {
        private readonly ISessionService _session;
        private readonly IUserRepository _userRepo;

        public EditarPerfilView()
        {
            InitializeComponent();
            _session = App.ServiceProvider.GetRequiredService<ISessionService>();
            _userRepo = App.ServiceProvider.GetRequiredService<IUserRepository>();

            Loaded += async (s, e) => await CargarDatosUsuario();
        }

        private async System.Threading.Tasks.Task CargarDatosUsuario()
        {
            var user = _session.CurrentUser;
            if (user == null) return;

            TxtEmail.Text = user.email;
            TxtEmailInput.Text = user.email;
            TxtDisplayName.Text = user.display_name ?? "";

            if (!string.IsNullOrEmpty(user.avatar_url))
            {
                try
                {
                    AvatarImage.Source = new BitmapImage(new Uri(user.avatar_url));
                }
                catch
                {
                    AvatarImage.Source = GenerateInitialsImage(user.display_name ?? user.email);
                }
            }
            else
            {
                AvatarImage.Source = GenerateInitialsImage(user.display_name ?? user.email);
            }
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            var user = _session.CurrentUser;
            if (user == null) return;

            var nuevoNombre = TxtDisplayName.Text?.Trim();

            if (string.IsNullOrEmpty(nuevoNombre))
            {
                MessageBox.Show("El nombre no puede estar vacío.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var actualizado = await _userRepo.UpdateUserAsync(user.id, nuevoNombre);
                if (actualizado != null)
                {
                    _session.CurrentUser = actualizado;
                    MessageBox.Show("Perfil actualizado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error al guardar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetInitials(string email)
        {
            if (string.IsNullOrEmpty(email)) return "?";
            var atIndex = email.IndexOf('@');
            var name = atIndex > 0 ? email.Substring(0, atIndex) : email;
            return name.Length > 0 ? char.ToUpper(name[0]).ToString() : "?";
        }

        private static Color GetColorForEmail(string email)
        {
            var colors = new[] {
                Color.FromRgb(0x00, 0x55, 0xCC),
                Color.FromRgb(0x00, 0x2A, 0x6E),
                Color.FromRgb(0x0B, 0x0F, 0x24),
                Color.FromRgb(0x6C, 0x63, 0xFF),
                Color.FromRgb(0xE0, 0x4F, 0x5F),
                Color.FromRgb(0x43, 0xE9, 0x7B),
                Color.FromRgb(0xF9, 0xA8, 0x25),
            };
            return colors[System.Math.Abs(email?.GetHashCode() ?? 0) % colors.Length];
        }

        private static BitmapSource GenerateInitialsImage(string email)
        {
            var initials = GetInitials(email);
            var color = GetColorForEmail(email);

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawEllipse(new SolidColorBrush(color), null, new Point(50, 50), 50, 50);

                var ft = new FormattedText(initials, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 40, Brushes.White, 96);
                ft.TextAlignment = TextAlignment.Center;
                ctx.DrawText(ft, new Point(50 - ft.Width / 2, 50 - ft.Height / 2));
            }

            var bitmap = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }
    }
}