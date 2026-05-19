using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PS2Desktop.Vistas
{
    public partial class AdministrarUsuariosView : UserControl
    {
        private readonly IUserRepository _userRepo;
        private readonly ISessionService _session;

        public AdministrarUsuariosView()
        {
            InitializeComponent();
            _userRepo = App.ServiceProvider.GetRequiredService<IUserRepository>();
            _session = App.ServiceProvider.GetRequiredService<ISessionService>();
            Loaded += async (s, e) => await CargarUsuarios();
        }

        private async System.Threading.Tasks.Task CargarUsuarios()
        {
            try
            {
                var users = await _userRepo.GetAllUsersAsync();
                if (users == null || users.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                    return;
                }

                EmptyState.Visibility = Visibility.Collapsed;
                var items = users.Select(u => new UsuarioItem
                {
                    Id = u.id,
                    Email = u.email,
                    DisplayName = u.display_name ?? u.email,
                    Role = u.role,
                    RoleLabel = u.role == "admin" ? "Admin" : "Usuario",
                    RoleColor = u.role == "admin" 
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0xCC))
                        : new SolidColorBrush(Color.FromRgb(0x5A, 0x6A, 0x78)),
                    CanChangeRole = u.id != _session.CurrentUser?.id,
                    ToggleRoleLabel = u.role == "admin" ? "Hacer usuario" : "Hacer admin",
                    AvatarSource = GenerarAvatar(u)
                }).ToList();

                UsersList.ItemsSource = items;
            }
            catch (Exception ex)
            {
                EmptyState.Text = $"Error al cargar usuarios: {ex.Message}";
                EmptyState.Visibility = Visibility.Visible;
            }
        }

        private BitmapSource GenerarAvatar(User user)
        {
            if (!string.IsNullOrEmpty(user.avatar_url))
            {
                try { return new BitmapImage(new Uri(user.avatar_url)); }
                catch (Exception ex) { LoggingService.Instance.Error("Error loading user avatar", ex); }
            }

            var name = user.display_name ?? user.email;
            var initials = string.IsNullOrEmpty(name) ? "?" : char.ToUpper(name[0]).ToString();
            var colors = new[] {
                Color.FromRgb(0x00, 0x55, 0xCC), Color.FromRgb(0x2A, 0x6E, 0x00),
                Color.FromRgb(0x6C, 0x63, 0xFF), Color.FromRgb(0xE0, 0x4F, 0x5F),
                Color.FromRgb(0x43, 0xE9, 0x7B), Color.FromRgb(0xF9, 0xA8, 0x25),
            };
            var color = colors[Math.Abs(user.email?.GetHashCode() ?? 0) % colors.Length];

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawEllipse(new SolidColorBrush(color), null, new Point(16, 16), 16, 16);
                var ft = new FormattedText(initials,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14,
                    Brushes.White, 96) { TextAlignment = TextAlignment.Center };
                ctx.DrawText(ft, new Point(16 - ft.Width / 2, 16 - ft.Height / 2));
            }
            var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        private async void BtnCambiarRol_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Guid userId)
            {
                var user = (await _userRepo.GetAllUsersAsync()).FirstOrDefault(u => u.id == userId);
                if (user == null) return;

                var nuevoRol = user.role == "admin" ? "user" : "admin";
                var mensaje = $"¿Estás seguro de cambiar el rol de {user.display_name ?? user.email} a {(nuevoRol == "admin" ? "Admin" : "Usuario")}?";

                if (MessageBox.Show(mensaje, "Confirmar cambio", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await _userRepo.UpdateUserRoleAsync(userId, nuevoRol);
                    await CargarUsuarios();
                }
            }
        }
    }

    public class UsuarioItem
    {
        public Guid Id { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public string RoleLabel { get; set; }
        public SolidColorBrush RoleColor { get; set; }
        public bool CanChangeRole { get; set; }
        public string ToggleRoleLabel { get; set; }
        public BitmapSource AvatarSource { get; set; }
    }
}