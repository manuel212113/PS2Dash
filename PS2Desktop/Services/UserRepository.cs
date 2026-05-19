using System.Collections.Generic;
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
        private static readonly HashSet<string> _adminEmails = new(StringComparer.OrdinalIgnoreCase)
        {
            "manuelintoroxd55@gmail.com"
        };

        public UserRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

        public async Task<User> CreateUserAsync(string email, string password)
        {
            var hashed = HashPassword(password);
            var id = Guid.NewGuid();
            var role = _adminEmails.Contains(email) ? "admin" : "user";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "INSERT INTO public.users (id, email, password_hash, role) VALUES (@id, @email, @hash, @role) RETURNING id, email, role, created_at";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@hash", hashed);
            cmd.Parameters.AddWithValue("@role", role);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    id = reader.GetGuid(0),
                    email = reader.GetString(1),
                    role = reader.IsDBNull(2) ? "user" : reader.GetString(2),
                    created_at = reader.GetDateTime(3)
                };
            }
            return null;
        }

        public async Task<User> AuthenticateUserAsync(string email, string password)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, email, password_hash, avatar_url, role, created_at FROM public.users WHERE email = @email LIMIT 1";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@email", email);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var hash = reader.GetString(2);
                if (VerifyPassword(password, hash))
                {
                    var role = reader.IsDBNull(4) ? "user" : reader.GetString(4);
                    
                    // Auto-promover a admin si el email está en la lista
                    if (_adminEmails.Contains(email) && role != "admin")
                    {
                        var userId = reader.GetGuid(0);
                        await reader.CloseAsync();
                        await UpdateUserRoleAsync(userId, "admin");
                        return await AuthenticateUserAsync(email, password);
                    }

                    return new User
                    {
                        id = reader.GetGuid(0),
                        email = reader.GetString(1),
                        password_hash = hash,
                        avatar_url = reader.IsDBNull(3) ? null : reader.GetString(3),
                        role = role,
                        created_at = reader.GetDateTime(5)
                    };
                }
            }
            return null;
        }

        public async Task<User> FindOrCreateUserWithGoogleAsync(string googleId, string email, string name, string? avatarUrl)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"SELECT id, email, password_hash, avatar_url, google_id, display_name, role, created_at
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
                    role = reader.IsDBNull(6) ? "user" : reader.GetString(6),
                    created_at = reader.GetDateTime(7)
                };

                // Auto-promover a admin si el email está en la lista
                if (_adminEmails.Contains(email) && user.role != "admin")
                {
                    await reader.CloseAsync();
                    await UpdateUserRoleAsync(user.id, "admin");
                    user.role = "admin";
                    return user;
                }

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
            var role = _adminEmails.Contains(email) ? "admin" : "user";
            var insert = @"INSERT INTO public.users (id, email, avatar_url, google_id, display_name, role)
                           VALUES (@id, @email, @avatarUrl, @googleId, @name, @role)
                           RETURNING id, email, avatar_url, google_id, display_name, role, created_at";
            await using var cmd3 = new NpgsqlCommand(insert, conn);
            cmd3.Parameters.AddWithValue("@id", id);
            cmd3.Parameters.AddWithValue("@email", email);
            cmd3.Parameters.AddWithValue("@avatarUrl", (object?)avatarUrl ?? DBNull.Value);
            cmd3.Parameters.AddWithValue("@googleId", googleId);
            cmd3.Parameters.AddWithValue("@name", name);
            cmd3.Parameters.AddWithValue("@role", role);
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
                    role = r2.IsDBNull(5) ? "user" : r2.GetString(5),
                    created_at = r2.GetDateTime(6)
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

        public async Task<User> UpdateUserAsync(Guid userId, string displayName)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = @"UPDATE public.users SET display_name = @name WHERE id = @id 
                        RETURNING id, email, avatar_url, display_name, role, created_at";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.Parameters.AddWithValue("@name", displayName);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    id = reader.GetGuid(0),
                    email = reader.GetString(1),
                    avatar_url = reader.IsDBNull(2) ? null : reader.GetString(2),
                    display_name = reader.IsDBNull(3) ? null : reader.GetString(3),
                    role = reader.IsDBNull(4) ? "user" : reader.GetString(4),
                    created_at = reader.GetDateTime(5)
                };
            }
            return null;
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            var users = new List<User>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, email, avatar_url, display_name, role, created_at FROM public.users ORDER BY created_at DESC";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                users.Add(new User
                {
                    id = reader.GetGuid(0),
                    email = reader.GetString(1),
                    avatar_url = reader.IsDBNull(2) ? null : reader.GetString(2),
                    display_name = reader.IsDBNull(3) ? null : reader.GetString(3),
                    role = reader.IsDBNull(4) ? "user" : reader.GetString(4),
                    created_at = reader.GetDateTime(5)
                });
            }
            return users;
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, email, avatar_url, display_name, role, created_at FROM public.users WHERE email = @email LIMIT 1";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@email", email);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    id = reader.GetGuid(0),
                    email = reader.GetString(1),
                    avatar_url = reader.IsDBNull(2) ? null : reader.GetString(2),
                    display_name = reader.IsDBNull(3) ? null : reader.GetString(3),
                    role = reader.IsDBNull(4) ? "user" : reader.GetString(4),
                    created_at = reader.GetDateTime(5)
                };
            }
            return null;
        }

        public async Task UpdateUserRoleAsync(Guid userId, string role)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "UPDATE public.users SET role = @role WHERE id = @id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.Parameters.AddWithValue("@role", role);
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

        public async Task<string> GenerateResetTokenAsync(string email)
        {
            var tokenBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(tokenBytes);
            var token = Convert.ToHexString(tokenBytes);
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "UPDATE public.users SET reset_token = @token, reset_token_expiry = NOW() + INTERVAL '15 minutes' WHERE email = @email";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@token", token);
            cmd.Parameters.AddWithValue("@email", email);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0 ? token : null;
        }

        public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id FROM public.users WHERE email = @email AND reset_token = @token AND reset_token_expiry > NOW() LIMIT 1";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@token", token);
            var result = await cmd.ExecuteScalarAsync();
            if (result == null) return false;

            var hashed = HashPassword(newPassword);
            var update = "UPDATE public.users SET password_hash = @hash, reset_token = NULL, reset_token_expiry = NULL WHERE id = @id";
            await using var cmd2 = new NpgsqlCommand(update, conn);
            cmd2.Parameters.AddWithValue("@hash", hashed);
            cmd2.Parameters.AddWithValue("@id", (Guid)result);
            await cmd2.ExecuteNonQueryAsync();
            return true;
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
