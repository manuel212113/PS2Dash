using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using Npgsql;
using PS2Desktop.Modelos;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class ThemeRepository : IThemeRepository
    {
        private readonly string _connectionString;
        private static readonly Dictionary<string, string> SortClauses = new()
        {
            ["date_asc"] = "created_at ASC",
            ["name_asc"] = "nombre ASC",
            ["name_desc"] = "nombre DESC",
        };

        public ThemeRepository(DatabaseInitializer dbInit)
        {
            _connectionString = dbInit.ConnectionString;
        }

        public async Task<List<Theme>> GetThemesAsync(int limit = 50, int offset = 0, string? search = null, string? sortBy = null)
        {
            var list = new List<Theme>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var where = "";
            if (!string.IsNullOrWhiteSpace(search))
                where = "WHERE (nombre ILIKE @search OR autor ILIKE @search)";
            if (!SortClauses.TryGetValue(sortBy ?? "", out var order))
                order = "created_at DESC";
            var sql = $"SELECT id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url, created_at FROM public.themes {where} ORDER BY {order} LIMIT @limit OFFSET @offset";
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var caracteristicas = new List<string>();
                if (!reader.IsDBNull(4))
                {
                    try
                    {
                        var json = reader.GetString(4);
                        if (!string.IsNullOrWhiteSpace(json))
                            caracteristicas = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ThemeRepo] Error deserializing: {ex.Message}"); }
                }

                list.Add(new Theme
                {
                    id = reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0),
                    nombre = reader.IsDBNull(1) ? null : reader.GetString(1),
                    autor = reader.IsDBNull(2) ? null : reader.GetString(2),
                    descripcion = reader.IsDBNull(3) ? null : reader.GetString(3),
                    caracteristicas = caracteristicas,
                    video_demo = reader.IsDBNull(5) ? null : reader.GetString(5),
                    link_descarga = reader.IsDBNull(6) ? null : reader.GetString(6),
                    image_url = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
            return list;
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
            cmd.Parameters.AddWithValue("@caracteristicas", JsonSerializer.Serialize(theme.caracteristicas ?? new List<string>()));
            cmd.Parameters.AddWithValue("@video", theme.video_demo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@link", theme.link_descarga ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@img", theme.image_url ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
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

        public async Task<int> GetThemeCountAsync(string? search = null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var where = "";
            if (!string.IsNullOrWhiteSpace(search))
                where = "WHERE (nombre ILIKE @search OR autor ILIKE @search)";
            var sql = $"SELECT COUNT(*) FROM public.themes {where}";
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            var result = await cmd.ExecuteScalarAsync();
            return result is long l ? (int)l : 0;
        }
    }
}
