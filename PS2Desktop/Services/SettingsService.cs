using Npgsql;
using PS2Desktop.Modelos;
using System.Windows;

namespace PS2Desktop.Services
{
    public static class AppSettings
    {
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
        public static bool IsLightMode => _cached?.IsLightMode ?? false;

        public static async Task LoadAsync()
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
            catch { }
        }

        public static async Task SaveAsync(double gameWidth, double gameHeight, double themeWidth, double themeHeight, bool isLightMode)
        {
            if (string.IsNullOrEmpty(_connectionString)) return;
            _cached = new AppSettingsData
            {
                GameCardWidth = gameWidth,
                GameCardHeight = gameHeight,
                ThemeCardWidth = themeWidth,
                ThemeCardHeight = themeHeight,
                IsLightMode = isLightMode
            };
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                var sql = @"INSERT INTO public.app_settings (id, game_card_width, game_card_height, theme_card_width, theme_card_height, is_light_mode)
                    VALUES (1, @gw, @gh, @tw, @th, @lm)
                    ON CONFLICT (id) DO UPDATE SET 
                        game_card_width = @gw, game_card_height = @gh, 
                        theme_card_width = @tw, theme_card_height = @th, 
                        is_light_mode = @lm";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@gw", gameWidth);
                cmd.Parameters.AddWithValue("@gh", gameHeight);
                cmd.Parameters.AddWithValue("@tw", themeWidth);
                cmd.Parameters.AddWithValue("@th", themeHeight);
                cmd.Parameters.AddWithValue("@lm", isLightMode);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
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