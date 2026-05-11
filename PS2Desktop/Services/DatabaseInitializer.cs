using System.Data;
using System.IO;
using System.Text.Json;
using Npgsql;

namespace PS2Desktop.Services
{
    public class DatabaseInitializer
    {
        private readonly string _connectionString;

        public DatabaseInitializer(string connectionString)
        {
            _connectionString = connectionString;
        }

        public static async Task<DatabaseInitializer> FromAppSettingsAsync(string appsettingsPath = "appsettings.json")
        {
            var json = await File.ReadAllTextAsync(appsettingsPath).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) && cs.TryGetProperty("DefaultConnection", out var def))
            {
                return new DatabaseInitializer(def.GetString());
            }
            throw new InvalidOperationException("DefaultConnection not found in appsettings.json");
        }

        public string ConnectionString => _connectionString;

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

CREATE TABLE IF NOT EXISTS public.download_links (
    id uuid PRIMARY KEY,
    game_id uuid REFERENCES public.games(id) ON DELETE CASCADE,
    url text NOT NULL,
    direct_url text,
    file_name text,
    file_size bigint,
    status text DEFAULT 'pending',
    created_at timestamptz DEFAULT now()
);

ALTER TABLE public.download_links ADD COLUMN IF NOT EXISTS image_url text;
";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
