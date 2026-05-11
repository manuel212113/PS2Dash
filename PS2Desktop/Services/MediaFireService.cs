using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PS2Desktop.Services
{
    public class MediaFireService
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" } }
        };

        public async Task<(string directUrl, string fileName, long? fileSize)> ResolveAsync(string url)
        {
            var response = await _http.GetStringAsync(url);
            var directUrl = ExtractDirectUrl(response);
            var fileName = ExtractFileName(response, url);
            var fileSize = ExtractFileSize(response);
            return (directUrl, fileName, fileSize);
        }

        private static string ExtractDirectUrl(string html)
        {
            var match = Regex.Match(html, @"https?://download[^""']+\.mediafire[^""']+");
            if (match.Success) return match.Value;

            match = Regex.Match(html, @"href\s*=\s*""(https?://download[^""]+)""");
            if (match.Success) return match.Groups[1].Value;

            match = Regex.Match(html, @"aria-label=""Download file""\s+href=""([^""]+)""");
            if (match.Success) return match.Groups[1].Value;

            return null;
        }

        private static string ExtractFileName(string html, string fallbackUrl)
        {
            // Try from page title
            var match = Regex.Match(html, @"<title>(.+?)</title>");
            if (match.Success)
            {
                var title = match.Groups[1].Value;
                // Remove only trailing " - MediaFire" or "MediaFire" suffix
                title = Regex.Replace(title, @"\s*[-–—]\s*MediaFire\s*$", "");
                title = Regex.Replace(title, @"^\s*MediaFire\s*[-–—]\s*", "");
                title = title.Trim();
                if (!string.IsNullOrEmpty(title)) return title;
            }

            // Fallback: extract from direct download URL in the page
            match = Regex.Match(html, @"https?://download[^""']+\.mediafire[^""']+");
            if (match.Success)
            {
                var path = match.Value;
                var segments = path.TrimEnd('/').Split('/');
                var last = segments.Length > 0 ? segments[^1] : null;
                if (!string.IsNullOrEmpty(last) && last.Contains('.')) return last;
            }

            // Last resort: from the original URL
            var segs = fallbackUrl.TrimEnd('/').Split('/');
            return segs.Length > 0 ? segs[^1] : "unknown";
        }

        private static long? ExtractFileSize(string html)
        {
            var match = Regex.Match(html, @"(\d+(?:\.\d+)?)\s*(KB|MB|GB)", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            var value = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var unit = match.Groups[2].Value.ToUpper();
            return unit switch
            {
                "KB" => (long)(value * 1024),
                "MB" => (long)(value * 1024 * 1024),
                "GB" => (long)(value * 1024 * 1024 * 1024),
                _ => (long)value
            };
        }
    }
}
