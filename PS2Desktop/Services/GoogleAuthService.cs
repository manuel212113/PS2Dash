using Google.Apis.Auth.OAuth2;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PS2Desktop.Services
{
    public class GoogleUserInfo
    {
        public string GoogleId { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
    }

    public static class GoogleAuthService
    {
        private static string _clientId;
        private static string _clientSecret;
        private static readonly HttpClient _http = new HttpClient();

        public static void Configure(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        public static bool IsConfigured =>
            !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret);

        public static async Task<GoogleUserInfo> LoginAsync()
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "Google OAuth no está configurado. Revisa appsettings.json.");

            var secrets = new ClientSecrets
            {
                ClientId = _clientId,
                ClientSecret = _clientSecret
            };

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets,
                    new[] { "openid", "email", "profile" },
                    "user",
                    CancellationToken.None);

                var accessToken = credential.Token.AccessToken;
                if (string.IsNullOrEmpty(accessToken))
                    throw new InvalidOperationException("No se recibió un token de acceso de Google.");

                using var request = new HttpRequestMessage(
                    HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _http.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    ClearDataStore();
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new GoogleUserInfo
                {
                    GoogleId = root.TryGetProperty("id", out var id) ? id.GetString() : "",
                    Email = root.TryGetProperty("email", out var email) ? email.GetString() : "",
                    Name = root.TryGetProperty("name", out var name) ? name.GetString() : "",
                    AvatarUrl = root.TryGetProperty("picture", out var pic) ? pic.GetString() : ""
                };
            }

            throw new InvalidOperationException(
                "No se pudo autenticar con Google después de varios intentos.");
        }

        public static void ClearDataStore()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Google.Apis.Auth");
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    try { File.Delete(file); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GoogleAuth] Error deleting temp file: {ex.Message}"); }
                }
            }
        }
    }
}
