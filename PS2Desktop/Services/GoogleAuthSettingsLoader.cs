using System.IO;
using System.Text.Json;

namespace PS2Desktop.Services
{
    public class GoogleAuthSettingsLoader
    {
        public void LoadInto(Action<string, string> configureAction)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return;

            var json = JsonDocument.Parse(File.ReadAllText(path));
            var google = json.RootElement.TryGetProperty("GoogleOAuth", out var o) ? o : default;
            if (google.ValueKind != JsonValueKind.Object) return;

            var clientId = google.TryGetProperty("ClientId", out var cid) ? cid.GetString() : null;
            var clientSecret = google.TryGetProperty("ClientSecret", out var cs) ? cs.GetString() : null;

            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret)
                && clientId != "REEMPLAZA_CON_TU_CLIENT_ID")
            {
                configureAction(clientId, clientSecret);
            }
        }
    }
}
