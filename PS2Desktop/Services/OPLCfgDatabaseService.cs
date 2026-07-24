using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PS2Desktop.Services
{
    public class OPLCfgDatabaseService
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string BaseUrl = "https://raw.githubusercontent.com/Tom-Bruise/PS2-OPL-CFG-Database/master/CFG_en";

        public class CfgData
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string Genre { get; set; } = "";
            public string Developer { get; set; } = "";
            public string Release { get; set; } = "";
            public string Vmode { get; set; } = "";
            public string Aspect { get; set; } = "";
            public string Compatibility { get; set; } = "";
            public string RawContent { get; set; } = "";
        }

        public static string GetCfgPath(string rootPath, string gameId)
        {
            string cfgDir = Path.Combine(rootPath, "CFG");
            return Path.Combine(cfgDir, $"{gameId}.cfg");
        }

        public static bool CfgExists(string rootPath, string gameId)
        {
            return File.Exists(GetCfgPath(rootPath, gameId));
        }

        public static CfgData? ReadLocalCfg(string rootPath, string gameId)
        {
            string cfgPath = GetCfgPath(rootPath, gameId);
            if (!File.Exists(cfgPath)) return null;

            try
            {
                string content = File.ReadAllText(cfgPath);
                return ParseCfg(content);
            }
            catch { return null; }
        }

        public static async Task<(bool success, CfgData? data, string message)> DownloadAndSaveCfgAsync(
            string rootPath, string gameId)
        {
            if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(gameId))
                return (false, null, "Ruta o gameId vacío");

            string cfgPath = GetCfgPath(rootPath, gameId);

            if (File.Exists(cfgPath))
                return (false, null, "El CFG ya existe");

            try
            {
                string url = $"{BaseUrl}/{gameId}.cfg";
                _http.DefaultRequestHeaders.UserAgent.Clear();
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("PS2Desktop/1.0");

                string content = await _http.GetStringAsync(url);

                string cfgDir = Path.GetDirectoryName(cfgPath) ?? "";
                if (!Directory.Exists(cfgDir))
                    Directory.CreateDirectory(cfgDir);

                await File.WriteAllTextAsync(cfgPath, content);

                var data = ParseCfg(content);
                return (true, data, "CFG descargado correctamente");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, null, $"No se encontró CFG para {gameId} en la base de datos");
            }
            catch (Exception ex)
            {
                return (false, null, $"Error descargando CFG: {ex.Message}");
            }
        }

        private static CfgData ParseCfg(string content)
        {
            var data = new CfgData { RawContent = content };
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx < 0) continue;

                string key = trimmed.Substring(0, eqIdx).Trim();
                string value = trimmed.Substring(eqIdx + 1).Trim();

                switch (key)
                {
                    case "Title": data.Title = value; break;
                    case "Description": data.Description = value; break;
                    case "Genre": data.Genre = value; break;
                    case "Developer": data.Developer = value; break;
                    case "Release": data.Release = value; break;
                    case "Vmode": data.Vmode = value; break;
                    case "Aspect": data.Aspect = value; break;
                    case "$Compatibility": data.Compatibility = value; break;
                }
            }

            return data;
        }
    }
}
