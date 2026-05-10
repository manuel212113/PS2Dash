using PS2Desktop.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PS2Desktop.Vistas
{
    public partial class LoginView : UserControl
    {
        public event EventHandler LoggedIn;

        private bool _isPasswordVisible;
        private bool _rememberChecked;

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

                // Configure Google OAuth from appsettings.json
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(path))
                {
                    var json = JsonDocument.Parse(File.ReadAllText(path));
                    var google = json.RootElement.TryGetProperty("GoogleOAuth", out var o)
                        ? o : default;
                    if (google.ValueKind == JsonValueKind.Object)
                    {
                        var clientId = google.TryGetProperty("ClientId", out var cid)
                            ? cid.GetString() : null;
                        var clientSecret = google.TryGetProperty("ClientSecret", out var cs)
                            ? cs.GetString() : null;

                        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret)
                            && clientId != "REEMPLAZA_CON_TU_CLIENT_ID")
                        {
                            GoogleAuthService.Configure(clientId, clientSecret);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", Brushes.Red);
            }
        }

        private async void BtnSignIn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var email = txtEmail.Text?.Trim();
            var pass = _isPasswordVisible ? txtPasswordVisible.Text : txtPassword.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                SetStatus("Correo y contraseña son requeridos.", Brushes.Red);
                return;
            }

            btnSignIn.IsEnabled = false;
            btnSignIn.Opacity = 0.5;
            btnSignIn.Background = new SolidColorBrush(Color.FromRgb(0, 0x44, 0xAA));
            BtnSignInText.Text = "Iniciando...";
            LblStatus.Text = "";
            SetStatus("Iniciando sesión...", Brushes.Gray);

            try
            {
                var user = await AppState.Db.AuthenticateUserAsync(email, pass);
                if (user != null)
                {
                    AppState.CurrentUser = user;
                    SetStatus("Sesión iniciada correctamente.", Brushes.YellowGreen);
                    LoggedIn?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    SetStatus("Credenciales inválidas.", Brushes.Red);
                    ResetSignInButton();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", Brushes.Red);
                ResetSignInButton();
            }
        }

        private void ResetSignInButton()
        {
            btnSignIn.IsEnabled = true;
            btnSignIn.Opacity = 1;
            btnSignIn.Background = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC));
            BtnSignInText.Text = "Iniciar sesión";
        }

        private async void BtnSignUp_Click(object sender, RoutedEventArgs e)
        {
            var email = txtEmail.Text?.Trim();
            var pass = _isPasswordVisible ? txtPasswordVisible.Text : txtPassword.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                SetStatus("Correo y contraseña son requeridos.", Brushes.Red);
                return;
            }

            btnSignUp.IsEnabled = false;
            SetStatus("Registrando...", Brushes.Gray);

            try
            {
                var user = await AppState.Db.CreateUserAsync(email, pass);
                if (user != null)
                {
                    AppState.CurrentUser = user;
                    SetStatus("Registro completado.", Brushes.YellowGreen);
                    LoggedIn?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    SetStatus("No se pudo registrar el usuario.", Brushes.Red);
                    btnSignUp.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", Brushes.Red);
                btnSignUp.IsEnabled = true;
            }
        }

        private async void BtnGoogle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!GoogleAuthService.IsConfigured)
            {
                SetStatus(
                    "Google OAuth no está configurado. Revisa appsettings.json con tus credenciales.",
                    Brushes.Red);
                return;
            }

            btnGoogle.IsEnabled = false;
            btnGoogle.Opacity = 0.5;
            BtnGoogleText.Text = "Conectando con Google...";
            SetStatus("", Brushes.Transparent);

            try
            {
                var googleUser = await GoogleAuthService.LoginAsync();

                var user = await AppState.Db.FindOrCreateUserWithGoogleAsync(
                    googleUser.GoogleId,
                    googleUser.Email,
                    googleUser.Name,
                    googleUser.AvatarUrl);

                if (user != null)
                {
                    AppState.CurrentUser = user;
                    SetStatus("Sesión iniciada con Google.", Brushes.YellowGreen);
                    LoggedIn?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    SetStatus("Error al crear usuario con Google.", Brushes.Red);
                    ResetGoogleButton();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error con Google: {ex.Message}", Brushes.Red);
                ResetGoogleButton();
            }
        }

        private void ResetGoogleButton()
        {
            btnGoogle.IsEnabled = true;
            btnGoogle.Opacity = 1;
            BtnGoogleText.Text = "o Inicia sesión con Google";
        }

        private void BtnTogglePassword_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                txtPasswordVisible.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Visibility = Visibility.Visible;
                PasswordToggleIcon.Text = "🙈";
            }
            else
            {
                txtPassword.Password = txtPasswordVisible.Text;
                txtPassword.Visibility = Visibility.Visible;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                PasswordToggleIcon.Text = "👁";
            }
        }

        private void BtnRemember_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _rememberChecked = !_rememberChecked;
            CheckMark.Visibility = _rememberChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Contacta al administrador para recuperar tu contraseña.", Brushes.Gray);
        }

        private void BtnSignIn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (btnSignIn.IsEnabled)
                btnSignIn.Background = new SolidColorBrush(Color.FromRgb(0, 0x66, 0xDD));
        }

        private void BtnSignIn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (btnSignIn.IsEnabled)
                btnSignIn.Background = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC));
        }

        private void BtnGoogle_MouseEnter(object sender, MouseEventArgs e)
        {
            if (btnGoogle.IsEnabled)
                btnGoogle.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        }

        private void BtnGoogle_MouseLeave(object sender, MouseEventArgs e)
        {
            if (btnGoogle.IsEnabled)
                btnGoogle.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        }

        private void Txt_GotFocus(object sender, RoutedEventArgs e)
        {
            EmailBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC));
        }

        private void Txt_LostFocus(object sender, RoutedEventArgs e)
        {
            EmailBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        }

        private void Pass_GotFocus(object sender, RoutedEventArgs e)
        {
            PassBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC));
        }

        private void Pass_LostFocus(object sender, RoutedEventArgs e)
        {
            PassBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        }

        private void SetStatus(string message, Brush color)
        {
            LblStatus.Text = message;
            LblStatus.Foreground = color;
        }
    }
}
