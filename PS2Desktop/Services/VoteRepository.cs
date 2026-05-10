using System.Data;
using Npgsql;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class VoteRepository : IVoteRepository
    {
        private readonly string _connectionString;

        public VoteRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

        public async Task<bool> VoteAsync(Guid itemId, string itemType, Guid userId, int value)
        {
            if (value < 1 || value > 5) throw new ArgumentOutOfRangeException(nameof(value));
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
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
