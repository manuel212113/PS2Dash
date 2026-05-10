using System.Data;
using System.Security.Cryptography;
using Npgsql;
using PS2Desktop.Modelos;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class UserRepository : IUserRepository
    {
        private readonly string _connectionString;

        public UserRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

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

        public async Task<int> GetUserCountAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT COUNT(*) FROM public.users";
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync();
            return result is long l ? (int)l : 0;
        }

        private static string HashPassword(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            byte[] salt = new byte[16];
            rng.GetBytes(salt);
            using var derive = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
            var key = derive.GetBytes(32);
            var result = new byte[1 + salt.Length + key.Length];
            result[0] = 0;
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
                using var derive = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
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
    }
}
