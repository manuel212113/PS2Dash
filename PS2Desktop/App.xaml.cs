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

            // Services
            services.AddSingleton<ISessionService, SessionService>();
            services.AddSingleton<IGoogleAuthService, GoogleAuthServiceWrapper>();
            services.AddSingleton<ISoundService, SoundServiceWrapper>();
            services.AddSingleton<GoogleAuthSettingsLoader>();

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
            }
            catch { }
        }
    }
}
