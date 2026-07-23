using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using PS2Desktop.Vistas;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop
{
    public partial class MainWindow : Window
    {
        private readonly ISessionService _session;
        private TemaView _currentTemaView;
        private bool _isNavigating = false;

        private void NavigateTo(object newContent)
        {
            if (_isNavigating) return;
            var current = MainContentFrame.Content;
            if (current == null || Equals(current, newContent))
            {
                MainContentFrame.Content = newContent;
                return;
            }

            _isNavigating = true;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fadeOut.Completed += (s, e) =>
            {
                DisposeCurrentContent();
                MainContentFrame.Content = newContent;

                MainContentFrame.Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2));
                fadeIn.Completed += (s2, e2) => _isNavigating = false;
                MainContentFrame.BeginAnimation(OpacityProperty, fadeIn);
            };
            MainContentFrame.BeginAnimation(OpacityProperty, fadeOut);
        }

        public MainWindow()
        {
            InitializeComponent();

            _session = App.ServiceProvider.GetRequiredService<ISessionService>();
            SoundService.Initialize();

            Loaded += async (s, e) =>
            {
                await AppSettings.LoadAsync();
                App.EnsureDarkTheme();
                ToastService.Instance.RegisterContainer(ToastContainer);
            };

            // 1. CARGA INICIAL: Cargamos la vista de login
            var loginView = new LoginView();
            loginView.LoggedIn += (s, e) =>
            {
                CargarHomeView();
                ActualizarPerfil();
            };
            MainContentFrame.Content = loginView;
        }

        // --- MÉTODOS DE NAVEGACIÓN ---

        private void DisposeCurrentContent()
        {
            if (MainContentFrame.Content is IDisposable d)
                d.Dispose();
        }

        private void MostrarLogin(Action onSuccess)
        {
            DisposeCurrentContent();
            var loginView = new LoginView();
            loginView.LoggedIn += (s, e) =>
            {
                ActualizarPerfil();
                onSuccess?.Invoke();
            };
            NavigateTo(loginView);
        }

        private bool VerificarLogin()
        {
            return _session.IsLoggedIn;
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            if (!VerificarLogin()) { MostrarLogin(CargarHomeView); return; }
            if (!_session.IsAdmin) return;
            CargarHomeView();
            ResaltarBotonActivo(BtnHome);
        }

        private void BtnTemas_Click(object sender, RoutedEventArgs e)
        {
            if (!VerificarLogin()) { MostrarLogin(CargarTemaView); return; }
            CargarTemaView();
            ResaltarBotonActivo(BtnTemas);
        }

        /// <summary>
        /// Carga la vista de temas con el manejador de eventos configurado
        /// </summary>
        private void CargarTemaView()
        {
            DisposeCurrentContent();
            _currentTemaView = new TemaView();
            _currentTemaView.IrADetalle += (s, tema) =>
            {
                if (tema != null)
                {
                    var detalleView = new DetalleTemaView();
                    detalleView.SetTema(tema);
                    detalleView.Volver += (s2, e2) => CargarTemaView();
                    NavigateTo(detalleView);
                }
            };
            NavigateTo(_currentTemaView);
        }

        /// <summary>
        /// Carga la vista Home con eventos de navegación
        /// </summary>
        private void CargarHomeView()
        {
            DisposeCurrentContent();
            var homeView = new HomeView();
            homeView.NavigateToTemas += (s, e) => CargarTemaView();
            homeView.NavigateToJuegos += (s, e) => CargarJuegosView();
            NavigateTo(homeView);
        }

        private void ResaltarBotonActivo(Button activo)
        {
            SoundService.PlayClick();
            var buttons = new[] { BtnHome, BtnTemas, BtnJuegos, BtnCrear, BtnHerramientas, BtnConfig, BtnAdmin, BtnPerfil };

            foreach (var btn in buttons)
            {
                if (btn == activo)
                {
                    btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252A3D"));
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888E9E"));
                }
            }
        }

        public void AplicarTema()
        {
            App.EnsureDarkTheme();
            Sidebar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121212"));
            foreach (var btn in new[] { BtnHome, BtnTemas, BtnJuegos, BtnCrear, BtnHerramientas, BtnConfig, BtnAdmin, BtnPerfil })
            {
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888E9E"));
            }
        }

        // --- CONTROLES DE LA VENTANA ---

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
            {
                this.MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
                this.WindowState = WindowState.Maximized;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (MainContentFrame.Content is JuegosView jv) { jv.FocusSearch(); e.Handled = true; }
                else if (MainContentFrame.Content is TemaView tv) { tv.FocusSearch(); e.Handled = true; }
            }
        }

        private void CargarJuegosView()
        {
            DisposeCurrentContent();
            var view = new JuegosView();
            view.IrADetalle += (s, juego) =>
            {
                if (juego != null)
                {
                    var detalle = new DetalleJuegosView();
                    detalle.SetGame(juego);
                    detalle.Volver += (s2, e2) => CargarJuegosView();
                    NavigateTo(detalle);
                }
            };
            NavigateTo(view);
            ResaltarBotonActivo(BtnJuegos);
        }

        private void BtnJuegos_Click(object sender, RoutedEventArgs e)
        {
            if (!VerificarLogin()) { MostrarLogin(CargarJuegosView); return; }
            CargarJuegosView();
        }

        private void BtnCrear_Click(object sender, RoutedEventArgs e)
        {
            if (!VerificarLogin()) { MostrarLogin(() => { NavigateTo(new CrearView()); }); return; }
            NavigateTo(new CrearView());
            ResaltarBotonActivo(BtnCrear);
        }

        private void BtnConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!_session.IsAdmin) return;
            NavigateTo(new ConfiguracionView());
            ResaltarBotonActivo(BtnConfig);
        }

        private void BtnHerramientas_Click(object sender, RoutedEventArgs e)
        {
            if (!VerificarLogin()) { MostrarLogin(() => CargarHerramientasView()); return; }
            CargarHerramientasView();
        }

        private void CargarHerramientasView()
        {
            DisposeCurrentContent();
            var view = new HerramientasView();
            view.Volver += (s, e2) => CargarHomeView();
            NavigateTo(view);
            ResaltarBotonActivo(BtnHerramientas);
        }

        private void BtnAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (!VerificarLogin() || !_session.IsAdmin) return;
            CargarAdminView();
            ResaltarBotonActivo(BtnAdmin);
        }

        private void CargarAdminView()
        {
            DisposeCurrentContent();
            NavigateTo(new AdministrarUsuariosView());
        }

        private void BtnPerfil_Click(object sender, RoutedEventArgs e)
        {
            if (!VerificarLogin()) { MostrarLogin(() => AbrirPerfilWindow()); return; }
            AbrirPerfilWindow();
            ResaltarBotonActivo(BtnPerfil);
        }

        private void AbrirPerfilWindow()
        {
            var perfilView = new PerfilView();
            if (perfilView.ShowDialog() == true)
                ActualizarPerfil();
        }

        private void ProfileSection_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_session.IsLoggedIn) return;
            var perfilView = new PerfilView();
            if (perfilView.ShowDialog() == true)
                ActualizarPerfil();
        }

        private void LogoutText_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _session.Logout();
            LogoutText.Visibility = Visibility.Collapsed;
            ProfileName.Text = "Usuario";
            AvatarImage.Source = null;
            var buttons = new[] { BtnHome, BtnTemas, BtnJuegos, BtnCrear, BtnHerramientas, BtnConfig, BtnAdmin, BtnPerfil };
            foreach (var btn in buttons)
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888E9E"));
            }
            MostrarLogin(CargarHomeView);
        }

        private void LogoutText_MouseEnter(object sender, MouseEventArgs e)
        {
            LogoutText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
        }

        private void LogoutText_MouseLeave(object sender, MouseEventArgs e)
        {
            LogoutText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888E9E"));
        }

        public void ActualizarPerfil()
        {
            var user = _session.CurrentUser;
            if (user == null) return;

            LogoutText.Visibility = Visibility.Visible;
            var display = user.display_name ?? user.email;
            ProfileName.Text = display;

            if (!string.IsNullOrEmpty(user.avatar_url))
            {
                try { AvatarImage.Source = new BitmapImage(new Uri(user.avatar_url)); }
                catch { AvatarImage.Source = GenerateInitialsImage(display); }
            }
            else
            {
                AvatarImage.Source = GenerateInitialsImage(display);
            }

            // Mostrar/ocultar botones según rol
            var esAdmin = _session.IsAdmin;
            BtnAdmin.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
            BtnHome.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
            BtnCrear.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
            BtnConfig.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
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
            return colors[Math.Abs(email?.GetHashCode() ?? 0) % colors.Length];
        }

        private static BitmapSource GenerateInitialsImage(string email)
        {
            var initials = GetInitials(email);
            var color = GetColorForEmail(email);

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawEllipse(new SolidColorBrush(color), null, new Point(22, 22), 22, 22);

                var ft = new FormattedText(initials, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 18, Brushes.White, 96);
                ft.TextAlignment = TextAlignment.Center;
                ctx.DrawText(ft, new Point(22 - ft.Width / 2, 22 - ft.Height / 2));
            }

            var bitmap = new RenderTargetBitmap(44, 44, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }
    }
}