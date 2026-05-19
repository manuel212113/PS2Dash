using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Media;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using PS2Desktop.ViewModels;
using PS2Desktop.Vistas;

namespace PS2Desktop
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }
        public static void EnsureDarkTheme()
        {
            Application.Current.Resources["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12));
            Application.Current.Resources["CardBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            Application.Current.Resources["TextMainBrush"] = new SolidColorBrush(Colors.White);
            Application.Current.Resources["TextSecundarioBrush"] = new SolidColorBrush(Color.FromRgb(0x88, 0x8E, 0x9E));
            Application.Current.Resources["InputBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x30));
            Application.Current.Resources["InputBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x42));
            Application.Current.Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x42));
            Application.Current.Resources["SidebarBrush"] = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12));
            Application.Current.Resources["CardAltBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x30));
            Application.Current.Resources["CardLightBrush"] = new SolidColorBrush(Color.FromRgb(0x13, 0x1A, 0x30));
            Application.Current.Resources["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x5A, 0x6A, 0x78));
            Application.Current.Resources["TextDimBrush"] = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x4A));
            Application.Current.Resources["InputBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            Application.Current.Resources["InputBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Global error handling
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var services = new ServiceCollection();

            // Database
            services.AddSingleton<DatabaseInitializer>(sp =>
                DatabaseInitializer.FromAppSettingsAsync(
                    AppSettings.AppSettingsPath
                ).GetAwaiter().GetResult());

            // Repositories
            services.AddSingleton<IUserRepository, UserRepository>();
            services.AddSingleton<IThemeRepository, ThemeRepository>();
            services.AddSingleton<IGameRepository, GameRepository>();
            services.AddSingleton<IAvatarRepository, AvatarRepository>();
            services.AddSingleton<IVoteRepository, VoteRepository>();
            services.AddSingleton<IDownloadRepository, DownloadRepository>();

            // Services
            services.AddSingleton<ISessionService, SessionService>();
            services.AddSingleton<IGoogleAuthService, GoogleAuthServiceWrapper>();
            services.AddSingleton<ISoundService, SoundServiceWrapper>();
            services.AddSingleton<GoogleAuthSettingsLoader>();
            services.AddSingleton<MediaFireService>();
            services.AddSingleton<IFavoriteRepository, FavoriteRepository>();
            services.AddSingleton<ImageCacheService>(_ => ImageCacheService.Instance);
            services.AddSingleton<EmailService>();
            services.AddSingleton<LoggingService>(_ => LoggingService.Instance);

            // ViewModels
            services.AddTransient<LoginViewModel>();
            services.AddTransient<HomeViewModel>();

            ServiceProvider = services.BuildServiceProvider();

            base.OnStartup(e);

            // Initialize DB
            CardVisualHelper.FireAndForget(() => InitializeDatabaseAsync(), "Error inicializando DB");

            var splash = new AppSplash();
            splash.Show();
        }

        private static async Task InitializeDatabaseAsync()
        {
            try
            {
                var dbInit = ServiceProvider.GetRequiredService<DatabaseInitializer>();
                await dbInit.InitializeAsync();
                AppSettings.Initialize(dbInit.ConnectionString);
                await AppSettings.LoadAsync();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Database init error: {ex.Message}"); }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            ShowErrorDialog("Error crítico", ex?.Message ?? "Ocurrió un error inesperado.");
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            ShowErrorDialog("Error de aplicación", e.Exception.Message);
            e.Handled = true;
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            ShowErrorDialog("Error en segundo plano", e.Exception?.InnerException?.Message ?? "Ocurrió un error en una tarea de fondo.");
            e.SetObserved();
        }

        private static void ShowErrorDialog(string title, string message)
        {
            try
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Error dialog error: {ex.Message}"); }
        }
    }
}
