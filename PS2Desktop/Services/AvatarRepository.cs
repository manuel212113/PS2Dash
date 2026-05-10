using System.Data;
using Npgsql;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class AvatarRepository : IAvatarRepository
    {
        private readonly string _connectionString;

        public AvatarRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

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
    }
}
