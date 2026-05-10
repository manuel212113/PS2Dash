using System.Data;
using System.Text.Json;
using Npgsql;
using PS2Desktop.Modelos;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class GameRepository : IGameRepository
    {
        private readonly string _connectionString;

        public GameRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

        public async Task<Game> GetGameByIdAsync(Guid id)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url, game_id, publisher, genero, fecha_lanzamiento, region, media_type, jugadores, resolucion, widescreen, created_at FROM public.games WHERE id = @id LIMIT 1";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapGame(reader);
            }
            return null;
        }

        public async Task<List<Game>> GetGamesAsync()
        {
            var list = new List<Game>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url, game_id, publisher, genero, fecha_lanzamiento, region, media_type, jugadores, resolucion, widescreen, created_at FROM public.games ORDER BY created_at DESC";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(MapGame(reader));
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
            cmd.Parameters.AddWithValue("@caracteristicas", JsonSerializer.Serialize(game.caracteristicas ?? new List<string>()));
            cmd.Parameters.AddWithValue("@video", game.video_demo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@link", game.link_descarga ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@img", game.image_url ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@game_id", game.game_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@jugadores", game.jugadores ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@resolucion", game.resolucion ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@widescreen", game.widescreen);
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

        public async Task<int> GetGameCountAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = "SELECT COUNT(*) FROM public.games";
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync();
            return result is long l ? (int)l : 0;
        }

        private static Game MapGame(NpgsqlDataReader reader)
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
                publisher = reader.IsDBNull(9) ? null : reader.GetString(9),
                genero = reader.IsDBNull(10) ? null : reader.GetString(10),
                fecha_lanzamiento = reader.IsDBNull(11) ? null : reader.GetString(11),
                region = reader.IsDBNull(12) ? null : reader.GetString(12),
                media_type = reader.IsDBNull(13) ? null : reader.GetString(13),
                jugadores = reader.IsDBNull(14) ? null : reader.GetString(14),
                resolucion = reader.IsDBNull(15) ? null : reader.GetString(15),
                widescreen = reader.GetBoolean(16),
                created_at = reader.GetDateTime(17)
            };
        }
    }
}
