using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using PS2Desktop.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PS2Desktop.Vistas
{
    public partial class LoginView : UserControl
    {
        public event EventHandler LoggedIn;

        private readonly IUserRepository _userRepo;
        private readonly ISessionService _session;
        private readonly IGoogleAuthService _googleAuth;
        private bool _isPasswordVisible;

        public LoginView()
        {
            InitializeComponent();

            _userRepo = App.ServiceProvider.GetRequiredService<IUserRepository>();
            _session = App.ServiceProvider.GetRequiredService<ISessionService>();
            _googleAuth = App.ServiceProvider.GetRequiredService<IGoogleAuthService>();

            this.Loaded += LoginView_Loaded;
        }

        private void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = App.ServiceProvider.GetRequiredService<GoogleAuthSettingsLoader>();
                settings.LoadInto(GoogleAuthServiceWrapper.Configure);
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
                var user = await _userRepo.AuthenticateUserAsync(email, pass);
                if (user != null)
                {
                    _session.CurrentUser = user;
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
                var user = await _userRepo.CreateUserAsync(email, pass);
                if (user != null)
                {
                    _session.CurrentUser = user;
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
            if (!_googleAuth.IsConfigured)
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
                var googleUser = await _googleAuth.LoginAsync();
                var user = await _userRepo.FindOrCreateUserWithGoogleAsync(
                    googleUser.GoogleId, googleUser.Email, googleUser.Name, googleUser.AvatarUrl);

                if (user != null)
                {
                    _session.CurrentUser = user;
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
            var chk = CheckMark.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            CheckMark.Visibility = chk;
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
