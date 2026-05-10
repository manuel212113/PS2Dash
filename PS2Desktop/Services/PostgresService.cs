using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;
using PS2Desktop.Modelos;

namespace PS2Desktop.Services
{
    // Requires NuGet packages: Npgsql
    // Optional: BCrypt.Net-Next if you prefer bcrypt. Here we use PBKDF2 for password hashing.
    public class PostgresService
    {
        private readonly string _connectionString;

        public PostgresService(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        // Read connection string from appsettings.json (simple parser)
        public static async Task<PostgresService> FromAppSettingsAsync(string appsettingsPath = "appsettings.json")
        {
            var json = await System.IO.File.ReadAllTextAsync(appsettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) && cs.TryGetProperty("DefaultConnection", out var def))
            {
                return new PostgresService(def.GetString());
            }
            throw new InvalidOperationException("DefaultConnection not found in appsettings.json");
        }

        // Create tables if they don't exist
        public async Task InitializeAsync()
        {
            var sql = @"CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.users (
    id uuid PRIMARY KEY,
    email text NOT NULL UNIQUE,
    password_hash text,
    created_at timestamptz DEFAULT now()
);

CREATE TABLE IF NOT EXISTS public.themes (
    id uuid PRIMARY KEY,
    nombre text NOT NULL,
    autor text,
    descripcion text,
    caracteristicas jsonb,
    video_demo text,
    link_descarga text,
    image_url text,
    created_at timestamptz DEFAULT now()
);

CREATE TABLE IF NOT EXISTS public.games (
    id uuid PRIMARY KEY,
    nombre text NOT NULL,
    autor text,
    descripcion text,
    caracteristicas jsonb,
    video_demo text,
    link_descarga text,
    image_url text,
    created_at timestamptz DEFAULT now()
);

CREATE TABLE IF NOT EXISTS public.votes (
    id uuid PRIMARY KEY,
    item_id uuid NOT NULL,
    item_type text NOT NULL,
    user_id uuid NOT NULL,
    value int NOT NULL CHECK (value >= 1 AND value <= 5),
    created_at timestamptz DEFAULT now(),
    UNIQUE (item_id, user_id, item_type)
);

CREATE TABLE IF NOT EXISTS public.avatars (
    id uuid PRIMARY KEY,
    nombre text NOT NULL,
    image_url text NOT NULL,
    created_at timestamptz DEFAULT now()
);

ALTER TABLE public.users ADD COLUMN IF NOT EXISTS avatar_url text;
ALTER TABLE public.users ADD COLUMN IF NOT EXISTS google_id text UNIQUE;
ALTER TABLE public.users ADD COLUMN IF NOT EXISTS display_name text;
ALTER TABLE public.users ALTER COLUMN password_hash DROP NOT NULL;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS game_id text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS publisher text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS genero text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS fecha_lanzamiento text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS region text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS media_type text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS jugadores text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS resolucion text;
ALTER TABLE public.games ADD COLUMN IF NOT EXISTS widescreen boolean default false;
";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            await cmd.ExecuteNonQueryAsync();
        }

        // Simple PBKDF2 hashing
        private static string HashPassword(string password)
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            byte[] salt = new byte[16];
            rng.GetBytes(salt);
            using var derive = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
            var key = derive.GetBytes(32);
            var result = new byte[1 + salt.Length + key.Length];
            result[0] = 0; // version
            Buffer.BlockCopy(salt, 0, result, 1, salt.Length);
            Buffer.BlockCopy(key, 0, result, 1 + salt.Length, key.Length);
            return Convert.ToBase64String(result);
        }

        private static bool VerifyPassword(string password, string stored)
        {
            try
            {
                var data = Convert.FromBase64String(stored);
                if (data.Length < 1 + 16 + 32) return false;
                var salt = new byte[16];
                Buffer.BlockCopy(data, 1, salt, 0, salt.Length);
                var key = new byte[32];
                Buffer.BlockCopy(data, 1 + salt.Length, key, 0, key.Length);
                using var derive = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
                var key2 = derive.GetBytes(32);
                return CryptographicEqual(key, key2);
            }
            catch
            {
                return false;
            }
        }

        private static bool CryptographicEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // User management
        public async Task<User> CreateUserAsync(string email, string password)
        {
            var hashed = HashPassword(password);
            var id = Guid.NewGuid();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "INSERT INTO public.users (id, email, password_hash) VALUES (@id, @email, @hash) RETURNING id, email, created_at";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@hash", hashed);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    id = reader.GetGuid(0),
                    email = reader.GetString(1),
                    created_at = reader.GetDateTime(2)
                };
            }
            return null;
        }

        public async Task<User> AuthenticateUserAsync(string email, string password)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, email, password_hash, avatar_url, created_at FROM public.users WHERE email = @email LIMIT 1";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@email", email);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var hash = reader.GetString(2);
                if (VerifyPassword(password, hash))
                {
                    return new User
                    {
                        id = reader.GetGuid(0),
                        email = reader.GetString(1),
                        password_hash = hash,
                        avatar_url = reader.IsDBNull(3) ? null : reader.GetString(3),
                        created_at = reader.GetDateTime(4)
                    };
                }
            }
            return null;
        }

        public async Task<User> FindOrCreateUserWithGoogleAsync(string googleId, string email, string name, string? avatarUrl)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Check if user exists by google_id or email
            var sql = @"SELECT id, email, password_hash, avatar_url, google_id, display_name, created_at
                        FROM public.users
                        WHERE google_id = @googleId OR email = @email
                        LIMIT 1";
            await using var find = new NpgsqlCommand(sql, conn);
            find.Parameters.AddWithValue("@googleId", googleId);
            find.Parameters.AddWithValue("@email", email);
            await using var reader = await find.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var user = new User
                {
                    id = reader.GetGuid(0),
                    email = reader.GetString(1),
                    password_hash = reader.IsDBNull(2) ? null : reader.GetString(2),
                    avatar_url = reader.IsDBNull(3) ? null : reader.GetString(3),
                    google_id = reader.IsDBNull(4) ? null : reader.GetString(4),
                    display_name = reader.IsDBNull(5) ? null : reader.GetString(5),
                    created_at = reader.GetDateTime(6)
                };

                // If found by email but no google_id linked, link it now
                if (user.google_id == null)
                {
                    await reader.CloseAsync();
                    var update = "UPDATE public.users SET google_id = @googleId, display_name = @name WHERE id = @id";
                    await using var cmd2 = new NpgsqlCommand(update, conn);
                    cmd2.Parameters.AddWithValue("@googleId", googleId);
                    cmd2.Parameters.AddWithValue("@name", name);
                    cmd2.Parameters.AddWithValue("@id", user.id);
                    await cmd2.ExecuteNonQueryAsync();
                    user.google_id = googleId;
                    user.display_name = name;
                }

                // Update avatar if Google has one and user has none
                if (avatarUrl != null && user.avatar_url == null)
                {
                    user.avatar_url = avatarUrl;
                    await reader.CloseAsync();
                    var update = "UPDATE public.users SET avatar_url = @url WHERE id = @id";
                    await using var cmd2 = new NpgsqlCommand(update, conn);
                    cmd2.Parameters.AddWithValue("@url", avatarUrl);
                    cmd2.Parameters.AddWithValue("@id", user.id);
                    await cmd2.ExecuteNonQueryAsync();
                }

                return user;
            }

            // Create new user with Google info
            await reader.CloseAsync();
            var id = Guid.NewGuid();
            var insert = @"INSERT INTO public.users (id, email, avatar_url, google_id, display_name)
                           VALUES (@id, @email, @avatarUrl, @googleId, @name)
                           RETURNING id, email, avatar_url, google_id, display_name, created_at";
            await using var cmd3 = new NpgsqlCommand(insert, conn);
            cmd3.Parameters.AddWithValue("@id", id);
            cmd3.Parameters.AddWithValue("@email", email);
            cmd3.Parameters.AddWithValue("@avatarUrl", (object?)avatarUrl ?? DBNull.Value);
            cmd3.Parameters.AddWithValue("@googleId", googleId);
            cmd3.Parameters.AddWithValue("@name", name);
            await using var r2 = await cmd3.ExecuteReaderAsync();
            if (await r2.ReadAsync())
            {
                return new User
                {
                    id = r2.GetGuid(0),
                    email = r2.GetString(1),
                    avatar_url = r2.IsDBNull(2) ? null : r2.GetString(2),
                    google_id = r2.IsDBNull(3) ? null : r2.GetString(3),
                    display_name = r2.IsDBNull(4) ? null : r2.GetString(4),
                    created_at = r2.GetDateTime(5)
                };
            }
            return null;
        }

        // Avatars
        public async Task<List<(Guid id, string nombre, string image_url)>> GetAvatarsAsync()
        {
            var list = new List<(Guid, string, string)>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, nombre, image_url FROM public.avatars ORDER BY nombre";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
            return list;
        }

        public async Task CreateAvatarAsync(string nombre, string imageUrl)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "INSERT INTO public.avatars (id, nombre, image_url) VALUES (@id, @nombre, @url)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@nombre", nombre);
            cmd.Parameters.AddWithValue("@url", imageUrl);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAvatarAsync(Guid id)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "DELETE FROM public.avatars WHERE id = @id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateUserAvatarAsync(Guid userId, string? avatarUrl)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "UPDATE public.users SET avatar_url = @url WHERE id = @id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.Parameters.AddWithValue("@url", (object?)avatarUrl ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // Themes
        public async Task<List<Theme>> GetThemesAsync()
        {
            var list = new List<Theme>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url, created_at FROM public.themes ORDER BY created_at DESC";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                List<string> caracteristicas = new List<string>();

                // Manejo seguro de características JSON
                if (!reader.IsDBNull(4))
                {
                    try
                    {
                        var caracteristicasJson = reader.GetString(4);
                        if (!string.IsNullOrWhiteSpace(caracteristicasJson))
                        {
                            caracteristicas = JsonSerializer.Deserialize<List<string>>(caracteristicasJson) ?? new List<string>();
                        }
                    }
                    catch
                    {
                        // Si falla la deserialización, usa lista vacía
                        caracteristicas = new List<string>();
                    }
                }

                var t = new Theme
                {
                    id = reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0),
                    nombre = reader.IsDBNull(1) ? null : reader.GetString(1),
                    autor = reader.IsDBNull(2) ? null : reader.GetString(2),
                    descripcion = reader.IsDBNull(3) ? null : reader.GetString(3),
                    caracteristicas = caracteristicas,
                    video_demo = reader.IsDBNull(5) ? null : reader.GetString(5),
                    link_descarga = reader.IsDBNull(6) ? null : reader.GetString(6),
                    image_url = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
                list.Add(t);
            }
            return list;
        }

        public async Task DeleteThemeAsync(Guid id)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "DELETE FROM public.themes WHERE id = @id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteGameAsync(Guid id)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "DELETE FROM public.games WHERE id = @id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CreateThemeAsync(Theme theme)
        {
            var id = Guid.NewGuid();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "INSERT INTO public.themes (id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url) VALUES (@id,@nombre,@autor,@descripcion,@caracteristicas::jsonb,@video,@link,@img)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@nombre", theme.nombre ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@autor", theme.autor ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@descripcion", theme.descripcion ?? (object)DBNull.Value);
            var charJson = JsonSerializer.Serialize(theme.caracteristicas ?? new List<string>());
            cmd.Parameters.AddWithValue("@caracteristicas", charJson);
            cmd.Parameters.AddWithValue("@video", theme.video_demo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@link", theme.link_descarga ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@img", theme.image_url ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // Games
        public async Task<Game> GetGameByIdAsync(Guid id)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url, game_id, created_at FROM public.games WHERE id = @id LIMIT 1";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Game
                {
                    id = reader.GetGuid(0),
                    nombre = reader.IsDBNull(1) ? null : reader.GetString(1),
                    autor = reader.IsDBNull(2) ? null : reader.GetString(2),
                    descripcion = reader.IsDBNull(3) ? null : reader.GetString(3),
                    caracteristicas = reader.IsDBNull(4) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(reader.GetString(4)),
                    video_demo = reader.IsDBNull(5) ? null : reader.GetString(5),
                    link_descarga = reader.IsDBNull(6) ? null : reader.GetString(6),
                    image_url = reader.IsDBNull(7) ? null : reader.GetString(7),
                    game_id = reader.IsDBNull(8) ? null : reader.GetString(8),
                    created_at = reader.GetDateTime(9)
                };
            }
            return null;
        }

        public async Task<List<Game>> GetGamesAsync()
        {
            var list = new List<Game>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url, game_id, created_at FROM public.games ORDER BY created_at DESC";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var g = new Game
                {
                    id = reader.GetGuid(0),
                    nombre = reader.IsDBNull(1) ? null : reader.GetString(1),
                    autor = reader.IsDBNull(2) ? null : reader.GetString(2),
                    descripcion = reader.IsDBNull(3) ? null : reader.GetString(3),
                    caracteristicas = reader.IsDBNull(4) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(reader.GetString(4)),
                    video_demo = reader.IsDBNull(5) ? null : reader.GetString(5),
                    link_descarga = reader.IsDBNull(6) ? null : reader.GetString(6),
                    image_url = reader.IsDBNull(7) ? null : reader.GetString(7),
                    game_id = reader.IsDBNull(8) ? null : reader.GetString(8),
                    created_at = reader.GetDateTime(9)
                };
                list.Add(g);
            }
            return list;
        }

        public async Task CreateGameAsync(Game game)
        {
            var id = game.id == Guid.Empty ? Guid.NewGuid() : game.id;
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = @"INSERT INTO public.games
                (id, nombre, autor, publisher, descripcion, genero, fecha_lanzamiento, region, media_type,
                 caracteristicas, video_demo, link_descarga, image_url, game_id, jugadores, resolucion, widescreen)
                VALUES (@id,@nombre,@autor,@publisher,@descripcion,@genero,@fecha,@region,@media,
                        @caracteristicas::jsonb,@video,@link,@img,@game_id,@jugadores,@resolucion,@widescreen)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@nombre", game.nombre ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@autor", game.autor ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@publisher", game.publisher ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@descripcion", game.descripcion ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@genero", game.genero ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@fecha", game.fecha_lanzamiento ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@region", game.region ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@media", game.media_type ?? (object)DBNull.Value);
            var charJson = JsonSerializer.Serialize(game.caracteristicas ?? new List<string>());
            cmd.Parameters.AddWithValue("@caracteristicas", charJson);
            cmd.Parameters.AddWithValue("@video", game.video_demo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@link", game.link_descarga ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@img", game.image_url ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@game_id", game.game_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@jugadores", game.jugadores ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@resolucion", game.resolucion ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@widescreen", game.widescreen);
            await cmd.ExecuteNonQueryAsync();
        }

        // Voting
        public async Task<bool> VoteAsync(Guid itemId, string itemType, Guid userId, int value)
        {
            if (value < 1 || value > 5) throw new ArgumentOutOfRangeException(nameof(value));
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            // Try insert; if conflict, update
            var sql = @"INSERT INTO public.votes (id, item_id, item_type, user_id, value) VALUES (@id,@item,@type,@user,@value)
ON CONFLICT (item_id, user_id, item_type) DO UPDATE SET value = EXCLUDED.value, created_at = now();";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@item", itemId);
            cmd.Parameters.AddWithValue("@type", itemType);
            cmd.Parameters.AddWithValue("@user", userId);
            cmd.Parameters.AddWithValue("@value", value);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<(double average, int count)> GetAverageRatingAsync(Guid itemId, string itemType)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT AVG(value)::float AS avg, COUNT(*) AS cnt FROM public.votes WHERE item_id = @item AND item_type = @type";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@item", itemId);
            cmd.Parameters.AddWithValue("@type", itemType);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var avg = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
                var cnt = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                return (avg, cnt);
            }
            return (0.0, 0);
        }

        // Home stats
        public async Task<int> GetThemeCountAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT COUNT(*) FROM public.themes";
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync();
            return result is long l ? (int)l : 0;
        }

        public async Task<int> GetGameCountAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT COUNT(*) FROM public.games";
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync();
            return result is long l ? (int)l : 0;
        }

        public async Task<int> GetUserCountAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT COUNT(*) FROM public.users";
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync();
            return result is long l ? (int)l : 0;
        }

        public async Task<(double average, int count)> GetGlobalAverageRatingAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT AVG(value)::float AS avg, COUNT(*) AS cnt FROM public.votes";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var avg = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
                var cnt = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                return (avg, cnt);
            }
            return (0.0, 0);
        }
    }
}
