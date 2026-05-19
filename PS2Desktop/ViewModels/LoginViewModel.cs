using System.IO;
using System.Text.Json;
using System.Windows.Input;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.ViewModels
{
    public class LoginViewModel : BaseViewModel
    {
        private readonly IUserRepository _userRepo;
        private readonly ISessionService _session;
        private readonly IGoogleAuthService _googleAuth;

        private string _email;
        private string _password;
        private string _statusText;
        private System.Windows.Media.Brush _statusColor;
        private bool _isLoading;
        private bool _isPasswordVisible;
        private bool _rememberChecked;

        public LoginViewModel(IUserRepository userRepo, ISessionService session, IGoogleAuthService googleAuth)
        {
            _userRepo = userRepo;
            _session = session;
            _googleAuth = googleAuth;
        }

        public string Email { get => _email; set { if (SetProperty(ref _email, value)) OnPropertyChanged(nameof(CanLogin)); } }
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
        public System.Windows.Media.Brush StatusColor { get => _statusColor; set => SetProperty(ref _statusColor, value); }
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool IsPasswordVisible { get => _isPasswordVisible; set => SetProperty(ref _isPasswordVisible, value); }
        public bool RememberChecked { get => _rememberChecked; set => SetProperty(ref _rememberChecked, value); }
        public bool CanLogin => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(_password);

        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                    OnPropertyChanged(nameof(CanLogin));
            }
        }

        public event Action LoginSucceeded;

        private void SetStatus(string message, System.Windows.Media.Brush color)
        {
            StatusText = message;
            StatusColor = color;
        }

        public bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(_password))
            {
                SetStatus("Correo y contraseña son requeridos.", System.Windows.Media.Brushes.Red);
                return false;
            }
            return true;
        }

        public async Task<User> LoginAsync()
        {
            if (!ValidateInput()) return null;

            IsLoading = true;
            SetStatus("Iniciando sesión...", System.Windows.Media.Brushes.Gray);

            try
            {
                var user = await _userRepo.AuthenticateUserAsync(Email, _password);
                if (user != null)
                {
                    _session.CurrentUser = user;
                    SetStatus("Sesión iniciada correctamente.", System.Windows.Media.Brushes.YellowGreen);
                    LoginSucceeded?.Invoke();
                    return user;
                }
                else
                {
                    SetStatus("Credenciales inválidas.", System.Windows.Media.Brushes.Red);
                    return null;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", System.Windows.Media.Brushes.Red);
                return null;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<User> RegisterAsync()
        {
            if (!ValidateInput()) return null;

            IsLoading = true;
            SetStatus("Registrando...", System.Windows.Media.Brushes.Gray);

            try
            {
                var user = await _userRepo.CreateUserAsync(Email, _password);
                if (user != null)
                {
                    _session.CurrentUser = user;
                    SetStatus("Registro completado.", System.Windows.Media.Brushes.YellowGreen);
                    LoginSucceeded?.Invoke();
                    return user;
                }
                else
                {
                    SetStatus("No se pudo registrar el usuario.", System.Windows.Media.Brushes.Red);
                    return null;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", System.Windows.Media.Brushes.Red);
                return null;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<User> LoginWithGoogleAsync()
        {
            if (!_googleAuth.IsConfigured)
            {
                SetStatus("Google OAuth no está configurado. Revisa appsettings.json con tus credenciales.", System.Windows.Media.Brushes.Red);
                return null;
            }

            IsLoading = true;
            SetStatus("", System.Windows.Media.Brushes.Transparent);

            try
            {
                var googleUser = await _googleAuth.LoginAsync();
                var user = await _userRepo.FindOrCreateUserWithGoogleAsync(
                    googleUser.GoogleId, googleUser.Email, googleUser.Name, googleUser.AvatarUrl);

                if (user != null)
                {
                    _session.CurrentUser = user;
                    SetStatus("Sesión iniciada con Google.", System.Windows.Media.Brushes.YellowGreen);
                    LoginSucceeded?.Invoke();
                    return user;
                }
                else
                {
                    SetStatus("Error al crear usuario con Google.", System.Windows.Media.Brushes.Red);
                    return null;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error con Google: {ex.Message}", System.Windows.Media.Brushes.Red);
                return null;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        public void ToggleRemember()
        {
            RememberChecked = !RememberChecked;
        }

        public void LoadGoogleConfig()
        {
            try
            {
                var path = AppSettings.AppSettingsPath;
                if (File.Exists(path))
                {
                    var json = JsonDocument.Parse(File.ReadAllText(path));
                    var google = json.RootElement.TryGetProperty("GoogleOAuth", out var o) ? o : default;
                    if (google.ValueKind == JsonValueKind.Object)
                    {
                        var clientId = google.TryGetProperty("ClientId", out var cid) ? cid.GetString() : null;
                        var clientSecret = google.TryGetProperty("ClientSecret", out var cs) ? cs.GetString() : null;

                        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret)
                            && clientId != "REEMPLAZA_CON_TU_CLIENT_ID")
                        {
                            GoogleAuthServiceWrapper.Configure(clientId, clientSecret);
                        }
                    }
                }
            }
            catch (Exception ex) { LoggingService.Instance.Error("Error loading Google config", ex); }
        }
    }
}
