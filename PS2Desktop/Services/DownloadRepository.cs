using Npgsql;
using PS2Desktop.Modelos;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class DownloadRepository : IDownloadRepository
    {
        private readonly string _connectionString;

        public DownloadRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

        public async Task<List<DownloadItem>> GetAllAsync()
        {
            var list = new List<DownloadItem>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, game_id, url, direct_url, file_name, file_size, status, created_at, image_url FROM public.download_links ORDER BY created_at DESC";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
            return list;
        }

        public async Task<List<DownloadItem>> GetByGameIdAsync(Guid gameId)
        {
            var list = new List<DownloadItem>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, game_id, url, direct_url, file_name, file_size, status, created_at, image_url FROM public.download_links WHERE game_id = @id ORDER BY created_at";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", gameId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
            return list;
        }

        public async Task CreateAsync(DownloadItem item)
        {
            var id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = @"INSERT INTO public.download_links (id, game_id, url, direct_url, file_name, file_size, status, image_url)
                VALUES (@id, @game_id, @url, @direct_url, @file_name, @file_size, @status, @image_url)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@game_id", (object)item.GameId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@url", item.Url ?? "");
            cmd.Parameters.AddWithValue("@direct_url", item.DirectUrl ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@file_name", item.FileName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@file_size", (object)item.FileSize ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", item.Status ?? "pending");
            cmd.Parameters.AddWithValue("@image_url", item.ImageUrl ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateAsync(DownloadItem item)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = @"UPDATE public.download_links SET direct_url=@direct_url, file_name=@file_name,
                file_size=@file_size, status=@status, image_url=@image_url WHERE id=@id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", item.Id);
            cmd.Parameters.AddWithValue("@direct_url", item.DirectUrl ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@file_name", item.FileName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@file_size", (object)item.FileSize ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", item.Status ?? "pending");
            cmd.Parameters.AddWithValue("@image_url", item.ImageUrl ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "DELETE FROM public.download_links WHERE id = @id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        private static DownloadItem Map(NpgsqlDataReader r) => new()
        {
            Id = r.GetGuid(0),
            GameId = r.IsDBNull(1) ? null : r.GetGuid(1),
            Url = r.IsDBNull(2) ? "" : r.GetString(2),
            DirectUrl = r.IsDBNull(3) ? null : r.GetString(3),
            FileName = r.IsDBNull(4) ? null : r.GetString(4),
            FileSize = r.IsDBNull(5) ? null : r.GetInt64(5),
            Status = r.IsDBNull(6) ? "pending" : r.GetString(6),
            CreatedAt = r.GetDateTime(7),
            ImageUrl = r.IsDBNull(8) ? null : r.GetString(8)
        };
    }
}
