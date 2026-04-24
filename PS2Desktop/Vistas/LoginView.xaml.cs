using PS2Desktop.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PS2Desktop.Vistas
{
    public partial class LoginView : UserControl
    {
        public event EventHandler LoggedIn;

        public LoginView()
        {
            InitializeComponent();
            this.Loaded += LoginView_Loaded;
        }

        private async void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AppState.Db == null)
                {
                    AppState.Db = await PostgresService.FromAppSettingsAsync();
                    await AppState.Db.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                ShowAlert($"Error iniciando servicio de base de datos: {ex.Message}", "Error");
            }
        }

        private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
        {
            var email = txtEmail.Text?.Trim();
            var pass = txtPassword.Password;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                ShowAlert("Email y contraseña son requeridos.", "Aviso");
                return;
            }

            try
            {
                var user = await AppState.Db.AuthenticateUserAsync(email, pass);
                if (user != null)
                {
                    AppState.CurrentUser = user;
                    ShowAlert("Sesión iniciada correctamente.", "Éxito");
                    LoggedIn?.Invoke(this, EventArgs.Empty);
                    Window.GetWindow(this)?.Close();
                }
                else
                {
                    ShowAlert("Credenciales inválidas.", "Error");
                }
            }
            catch (Exception ex)
            {
                ShowAlert($"Error al iniciar sesión: {ex.Message}", "Error");
            }
        }

        private async void BtnSignUp_Click(object sender, RoutedEventArgs e)
        {
            var email = txtEmail.Text?.Trim();
            var pass = txtPassword.Password;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                ShowAlert("Email y contraseña son requeridos.", "Aviso");
                return;
            }

            try
            {
                var user = await AppState.Db.CreateUserAsync(email, pass);
                if (user != null)
                {
                    AppState.CurrentUser = user;
                    ShowAlert("Registro completado. Has iniciado sesión.", "Éxito");
                    LoggedIn?.Invoke(this, EventArgs.Empty);
                    Window.GetWindow(this)?.Close();
                }
                else
                {
                    ShowAlert("No se pudo registrar el usuario.", "Error");
                }
            }
            catch (Exception ex)
            {
                ShowAlert($"Error al registrar: {ex.Message}", "Error");
            }
        }

        private void ShowAlert(string message, string title)
        {
            // Ventana de alerta personalizada simple
            var w = new Window
            {
                Title = title,
                Width = 350,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = Window.GetWindow(this),
                Content = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34,34,34)),
                    Padding = new Thickness(12),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) },
                            new Button { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true }
                        }
                    }
                }
            };

            // Wire OK button
            if (w.Content is Border bd && bd.Child is StackPanel sp && sp.Children[1] is Button btn)
            {
                btn.Click += (s, e) => w.Close();
            }

            w.ShowDialog();
        }
    }
}
