using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using PS2Desktop.ViewModels;
using PS2Desktop.Vistas;

namespace PS2Desktop
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }
        public static event Action<bool>? ThemeChanged;

        public static void ApplyTheme(bool isLightMode)
        {
            if (isLightMode)
            {
                Current.Resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5));
                Current.Resources["SidebarBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
                Current.Resources["CardBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
                Current.Resources["CardAltBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF0));
                Current.Resources["CardLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));
                Current.Resources["TextMainBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A));
                Current.Resources["TextSecundarioBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
                Current.Resources["TextMutedBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
                Current.Resources["TextDimBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xBB, 0xBB, 0xBB));
                Current.Resources["BorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD));
                Current.Resources["InputBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
                Current.Resources["InputBorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
            }
            else
            {
                Current.Resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x12));
                Current.Resources["SidebarBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x12));
                Current.Resources["CardBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20));
                Current.Resources["CardAltBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x20, 0x30));
                Current.Resources["CardLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x13, 0x1A, 0x30));
                Current.Resources["TextMainBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                Current.Resources["TextSecundarioBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x8E, 0x9E));
                Current.Resources["TextMutedBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5A, 0x6A, 0x78));
                Current.Resources["TextDimBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x4A));
                Current.Resources["BorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2F, 0x42));
                Current.Resources["InputBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A));
                Current.Resources["InputBorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x25));
            }
            ThemeChanged?.Invoke(isLightMode);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var services = new ServiceCollection();

            // Database
            services.AddSingleton<DatabaseInitializer>(sp =>
                DatabaseInitializer.FromAppSettingsAsync(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json")
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

            // ViewModels
            services.AddTransient<LoginViewModel>();
            services.AddTransient<HomeViewModel>();

            ServiceProvider = services.BuildServiceProvider();

            base.OnStartup(e);

            // Initialize DB
            _ = InitializeDatabaseAsync();

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
            catch { }
        }
    }
}
