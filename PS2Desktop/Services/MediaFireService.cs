using System;
using System.IO;
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
            var extension = ExtractExtensionFromUrl(html);

            // Try from page title
            var match = Regex.Match(html, @"<title>(.+?)</title>");
            if (match.Success)
            {
                var title = match.Groups[1].Value;
                title = Regex.Replace(title, @"\s*[-–—]\s*MediaFire\s*$", "");
                title = Regex.Replace(title, @"^\s*MediaFire\s*[-–—]\s*", "");
                title = title.Trim();
                if (!string.IsNullOrEmpty(title))
                {
                    if (!string.IsNullOrEmpty(extension) && !title.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        return title + extension;
                    return title;
                }
            }

            // Fallback: extract from direct download URL in the page
            match = Regex.Match(html, @"https?://download[^""']+\.mediafire[^""']+");
            if (match.Success)
            {
                var path = match.Value;
                var segments = path.TrimEnd('/').Split('/');
                var last = segments.Length > 0 ? segments[^1] : null;
                if (!string.IsNullOrEmpty(last))
                {
                    if (!last.Contains('.') && !string.IsNullOrEmpty(extension))
                        return last + extension;
                    return last;
                }
            }

            // Last resort: from the original URL
            var segs = fallbackUrl.TrimEnd('/').Split('/');
            var name = segs.Length > 0 ? segs[^1] : "unknown";
            if (!name.Contains('.') && !string.IsNullOrEmpty(extension))
                return name + extension;
            return name;
        }

        private static string ExtractExtensionFromUrl(string html)
        {
            var match = Regex.Match(html, @"https?://download[^""']+\.mediafire[^""']+\.(zip|rar|7z|exe|pdf|png|jpg|jpeg|gif|mp3|mp4|iso|bin|cso|nrg|mdf|ape|flac|wv|md5|sfv)", RegexOptions.IgnoreCase);
            if (match.Success)
                return "." + match.Groups[1].Value.ToLower();

            match = Regex.Match(html, @"""filename""[^>]+>\s*([^<]+(?:\.[a-zA-Z0-9]+))");
            if (match.Success)
            {
                var ext = Path.GetExtension(match.Groups[1].Value);
                if (!string.IsNullOrEmpty(ext)) return ext.ToLower();
            }

            return "";
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
