using Npgsql;
using PS2Desktop.Modelos;
using System.Windows;

namespace PS2Desktop.Services
{
    public static class AppSettings
    {
        public static readonly string AppSettingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static int PageSize { get; set; } = 20;
        public static int HttpTimeoutSeconds { get; set; } = 15;
        public static int MaxCacheItems { get; set; } = 100;
        public static int ImageConcurrency { get; set; } = 4;
        public static double ToastDurationSeconds { get; set; } = 3.5;

        private static string? _connectionString;
        private static AppSettingsData? _cached;

        public static void Initialize(string connectionString)
        {
            _connectionString = connectionString;
        }

        public static double GameCardWidth => _cached?.GameCardWidth ?? 230;
        public static double GameCardHeight => _cached?.GameCardHeight ?? 350;
        public static double ThemeCardWidth => _cached?.ThemeCardWidth ?? 230;
        public static double ThemeCardHeight => _cached?.ThemeCardHeight ?? 300;
        public static bool IsLightMode => false;

        public static async System.Threading.Tasks.Task LoadAsync()
        {
            if (string.IsNullOrEmpty(_connectionString)) return;
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                var sql = "SELECT game_card_width, game_card_height, theme_card_width, theme_card_height, is_light_mode FROM public.app_settings WHERE id = 1";
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    _cached = new AppSettingsData
                    {
                        GameCardWidth = reader.GetDouble(0),
                        GameCardHeight = reader.GetDouble(1),
                        ThemeCardWidth = reader.GetDouble(2),
                        ThemeCardHeight = reader.GetDouble(3),
                        IsLightMode = reader.GetBoolean(4)
                    };
                }
            }
            catch (Npgsql.PostgresException ex) { System.Diagnostics.Debug.WriteLine($"[Settings] DB error loading: {ex.Message}"); }
            catch (InvalidOperationException ex) { System.Diagnostics.Debug.WriteLine($"[Settings] Config error: {ex.Message}"); }
        }

        public static async System.Threading.Tasks.Task SaveAsync(double gameCardWidth, double gameCardHeight, double themeCardWidth, double themeCardHeight, bool isLightMode)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                var sql = "UPDATE public.app_settings SET game_card_width = @gcw, game_card_height = @gch, theme_card_width = @tcw, theme_card_height = @tch, is_light_mode = @lm WHERE id = 1";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@gcw", gameCardWidth);
                cmd.Parameters.AddWithValue("@gch", gameCardHeight);
                cmd.Parameters.AddWithValue("@tcw", themeCardWidth);
                cmd.Parameters.AddWithValue("@tch", themeCardHeight);
                cmd.Parameters.AddWithValue("@lm", isLightMode);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Npgsql.PostgresException ex) { System.Diagnostics.Debug.WriteLine($"[Settings] DB error saving: {ex.Message}"); }
        }

        private class AppSettingsData
        {
            public double GameCardWidth { get; set; }
            public double GameCardHeight { get; set; }
            public double ThemeCardWidth { get; set; }
            public double ThemeCardHeight { get; set; }
            public bool IsLightMode { get; set; }
        }
    }
}
