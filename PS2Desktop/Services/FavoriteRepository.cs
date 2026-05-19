using Npgsql;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class FavoriteRepository : IFavoriteRepository
    {
        private readonly string _connectionString;

        public FavoriteRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

        public async Task<bool> IsFavoriteAsync(Guid userId, Guid itemId, string itemType)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT COUNT(*) FROM public.user_favorites WHERE user_id = @uid AND item_id = @iid AND item_type = @type";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@iid", itemId);
            cmd.Parameters.AddWithValue("@type", itemType);
            var result = await cmd.ExecuteScalarAsync();
            return result is long l && l > 0;
        }

        public async Task ToggleFavoriteAsync(Guid userId, Guid itemId, string itemType)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var check = await IsFavoriteAsync(userId, itemId, itemType);
            if (check)
            {
                var sql = "DELETE FROM public.user_favorites WHERE user_id = @uid AND item_id = @iid AND item_type = @type";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@iid", itemId);
                cmd.Parameters.AddWithValue("@type", itemType);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var sql = "INSERT INTO public.user_favorites (id, user_id, item_id, item_type) VALUES (@id, @uid, @iid, @type)";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@iid", itemId);
                cmd.Parameters.AddWithValue("@type", itemType);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<(Guid ItemId, string ItemType)>> GetUserFavoritesAsync(Guid userId)
        {
            var list = new List<(Guid, string)>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT item_id, item_type FROM public.user_favorites WHERE user_id = @uid";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetGuid(0), reader.GetString(1)));
            return list;
        }
    }
}
