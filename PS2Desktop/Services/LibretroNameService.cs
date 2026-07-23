using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PS2Desktop.Services
{
    public class LibretroNameService
    {
        private const string DatUrl = "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/redump/Sony%20-%20PlayStation%202.dat";
        private static readonly Regex StripRegex = new(@"\((Beta|Proto|Unl|v[\d\.]+|Demo\s+\d+|Layer\s+\d+|Taikenban|Shokai\s+Genteiban)\)\s*$", RegexOptions.IgnoreCase);

        private Dictionary<string, string>? _serialToName;
        private readonly HttpClient _http = new();
        private readonly object _lock = new();
        private Task? _loadTask;
        private bool _loadedEventFired;

        public event Action? Loaded;

        public async Task EnsureLoadedAsync()
        {
            if (_serialToName != null) return;

            Task task;
            lock (_lock)
            {
                if (_serialToName != null) return;
                if (_loadTask == null)
                    _loadTask = LoadAsync();
                task = _loadTask;
            }

            await task;
        }

        public string? GetName(string? serial)
        {
            if (string.IsNullOrEmpty(serial) || _serialToName == null) return null;

            if (_serialToName.TryGetValue(serial, out var name))
                return name;

            // Normalize SYSTEM.CNF format to DAT format: SLUS_206.24 → SLUS-20624
            var normalized = serial.Replace('_', '-').Replace(".", "");
            if (normalized != serial && _serialToName.TryGetValue(normalized, out name))
                return name;

            // Try without common suffixes: GH, -P, /P, /ANZ, /GER, /1.00, /UK
            var trimmed = StripSerialSuffix(normalized != serial ? normalized : serial);
            if (trimmed != serial && _serialToName.TryGetValue(trimmed, out name))
                return name;

            // Try stripping trailing number after hyphen (e.g. SLPM-65002-0 → SLPM-65002)
            var dashIdx = serial.LastIndexOf('-');
            var slashIdx = serial.LastIndexOf('/');
            var lastSep = Math.Max(dashIdx, slashIdx);
            if (lastSep > 0 && lastSep < serial.Length - 1)
            {
                var suffix = serial.Substring(lastSep + 1);
                if (suffix.Length <= 3 && int.TryParse(suffix, out _))
                {
                    var baseSerial = serial.Substring(0, lastSep);
                    if (_serialToName.TryGetValue(baseSerial, out name))
                        return name;
                }
            }

            return null;
        }

        public bool IsLoaded => _serialToName != null;

        private async Task LoadAsync()
        {
            try
            {
                var content = await _http.GetStringAsync(DatUrl);
                var parsed = ParseDat(content);
                lock (_lock) { _serialToName = parsed; }
                System.Diagnostics.Debug.WriteLine($"[LibretroNameService] Loaded {parsed.Count} entries");
                if (!_loadedEventFired) { _loadedEventFired = true; Loaded?.Invoke(); }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibretroNameService] Error: {ex.Message}");
                lock (_lock) { _serialToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
            }
        }

        private static Dictionary<string, string> ParseDat(string content)
        {
            var result = new Dictionary<string, string>(20000, StringComparer.OrdinalIgnoreCase);
            string? currentName = null;
            string? currentSerial = null;
            bool inGame = false;

            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("game (", StringComparison.Ordinal))
                {
                    inGame = true;
                    currentName = null;
                    currentSerial = null;
                    continue;
                }

                if (!inGame) continue;

                if (trimmed == ")")
                {
                    inGame = false;
                    if (currentSerial != null && currentName != null)
                    {
                        var cleaned = StripNoiseSuffixes(currentName);
                        result[currentSerial] = cleaned;
                    }
                    continue;
                }

                if (trimmed.StartsWith("name ", StringComparison.Ordinal))
                    currentName = ExtractQuotedValue(trimmed);
                else if (trimmed.StartsWith("serial ", StringComparison.Ordinal) && currentSerial == null)
                    currentSerial = ExtractQuotedValue(trimmed);
            }

            return result;
        }

        internal static string StripNoiseSuffixes(string name)
        {
            return StripRegex.Replace(name, "").TrimEnd();
        }

        internal static string StripSerialSuffix(string serial)
        {
            return serial
                .Replace("GH", "")
                .Replace("-P", "")
                .Replace("/P", "")
                .Replace("/ANZ", "")
                .Replace("/GER", "")
                .Replace("/UK", "")
                .Replace("/AUS", "")
                .Replace("/1.00", "")
                .Replace("/2.00", "")
                .Trim();
        }

        private static string? ExtractQuotedValue(string line)
        {
            var start = line.IndexOf('"');
            var end = line.LastIndexOf('"');
            if (start >= 0 && end > start)
                return line.Substring(start + 1, end - start - 1);
            return null;
        }
    }
}
