using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PS2Desktop.Services
{
    public class OPLLibraryService
    {
        public static readonly string[] OplFolders = { "CD", "DVD", "VCD", "POPS", "APPS" };
        public static readonly string[] OplSupportFolders = { "ART", "CFG", "VMC" };

        public class LibraryGame
        {
            public string GameId { get; set; } = "";
            public string Name { get; set; } = "";
            public string System { get; set; } = "";
            public string FilePath { get; set; } = "";
            public long FileSize { get; set; }
            public string SizeDisplay => OPLService.FormatSize(FileSize);
            public string Format { get; set; } = "";
            public string CoverPath { get; set; } = "";
            public string IconPath { get; set; } = "";
            public string BackgroundPath { get; set; } = "";
            public string ScreenshotPath { get; set; } = "";
            public string[] ScreenshotPaths { get; set; } = Array.Empty<string>();
            public string Region { get; set; } = "";
            public bool IsUl { get; set; }
            public int Parts { get; set; }
            public DateTime LastModified { get; set; }
        }

        public class LibraryStats
        {
            public int TotalGames { get; set; }
            public int Ps2Dvd { get; set; }
            public int Ps2Cd { get; set; }
            public int Ps1 { get; set; }
            public int Apps { get; set; }
            public int UlGames { get; set; }
            public long TotalSize { get; set; }
            public string TotalSizeDisplay => OPLService.FormatSize(TotalSize);
        }

        public static async Task<(List<LibraryGame> games, LibraryStats stats)> ScanLibraryAsync(
            string rootPath, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var games = new List<LibraryGame>();
                var stats = new LibraryStats();

                if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                    return (games, stats);

                int totalFolders = OplFolders.Length + 1;
                int currentFolder = 0;

                foreach (var folder in OplFolders)
                {
                    ct.ThrowIfCancellationRequested();
                    currentFolder++;
                    progress?.Report((int)((double)currentFolder / totalFolders * 100));

                    string folderPath = Path.Combine(rootPath, folder);
                    if (!Directory.Exists(folderPath)) continue;

                    string system = folder switch
                    {
                        "CD" => "PS2 CD",
                        "DVD" => "PS2 DVD",
                        "VCD" => "PS1",
                        "POPS" => "PS1",
                        "APPS" => "APPS",
                        _ => folder
                    };

                    try
                    {
                        var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(f =>
                            {
                                string ext = Path.GetExtension(f).ToLowerInvariant();
                                return ext is ".iso" or ".zso" or ".bin" or ".cue" or ".vcd" or ".elf";
                            })
                            .ToList();

                        foreach (var file in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            var fi = new FileInfo(file);
                            string gameId = ExtractGameId(file);
                            string name = ExtractGameName(file, gameId);

                            var game = new LibraryGame
                            {
                                GameId = gameId,
                                Name = name,
                                System = system,
                                FilePath = file,
                                FileSize = fi.Length,
                                Format = Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),
                                LastModified = fi.LastWriteTime,
                                Region = GetRegionFromId(gameId)
                            };

                            FindArt(rootPath, game);
                            games.Add(game);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error scanning {folder}: {ex.Message}");
                    }
                }

                // Scan UL games
                currentFolder++;
                progress?.Report((int)((double)currentFolder / totalFolders * 100));
                try
                {
                    var ulGames = OPLService.ReadUlCfg(rootPath);
                    foreach (var ug in ulGames)
                    {
                        ct.ThrowIfCancellationRequested();
                        string gameId = ug.GameId;
                        string system = ug.Media == 0x12 ? "PS2 CD" : "PS2 DVD";

                        var game = new LibraryGame
                        {
                            GameId = gameId,
                            Name = ug.Name,
                            System = system,
                            FilePath = "",
                            FileSize = ug.SizeBytes,
                            Format = "UL",
                            IsUl = true,
                            Parts = ug.Parts,
                            Region = GetRegionFromId(gameId)
                        };

                        FindArt(rootPath, game);
                        games.Add(game);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading ul.cfg: {ex.Message}");
                }

                stats.TotalGames = games.Count;
                stats.Ps2Dvd = games.Count(g => g.System == "PS2 DVD");
                stats.Ps2Cd = games.Count(g => g.System == "PS2 CD");
                stats.Ps1 = games.Count(g => g.System == "PS1");
                stats.Apps = games.Count(g => g.System == "APPS");
                stats.UlGames = games.Count(g => g.IsUl);
                stats.TotalSize = games.Sum(g => g.FileSize);

                progress?.Report(100);
                return (games, stats);
            }, ct);
        }

        private static string ExtractGameId(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            // Try to match OPL naming: GAMEID.Title.ext or Title.ext
            var match = Regex.Match(fileName, @"^([A-Z]{4}_\d{3}\.\d{2})");
            if (match.Success)
                return match.Groups[1].Value;

            // Try without dot: GAMEIDTitle
            match = Regex.Match(fileName, @"^([A-Z]{4}_\d{3}\d{2})");
            if (match.Success)
                return match.Groups[1].Value;

            // Try ISO reader for actual disc images
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".iso" or ".zso")
            {
                try
                {
                    using var iso = new ISOReader();
                    iso.Init(filePath);
                    string id = iso.GetGameId();
                    if (!string.IsNullOrEmpty(id))
                        return id;
                }
                catch { }
            }

            // Fallback: use filename
            return fileName.Length > 20 ? fileName.Substring(0, 20) : fileName;
        }

        private static string ExtractGameName(string filePath, string gameId)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            // OPL naming: GAMEID.Title.ext
            if (fileName.Contains('.') && fileName.Length > gameId.Length + 1)
            {
                int dotIndex = fileName.IndexOf('.');
                if (dotIndex > 0 && dotIndex < fileName.Length - 1)
                {
                    string afterDot = fileName.Substring(dotIndex + 1).Trim();
                    if (afterDot.Length > 2)
                        return afterDot;
                }
            }

            // Fallback: use filename without extension
            return fileName;
        }

        private static string GetRegionFromId(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return "Unknown";
            string prefix = gameId.Length >= 4 ? gameId.Substring(0, 4) : gameId;
            return prefix switch
        {
            "SLUS" or "SCUS" => "NTSC-U",
            "SLPS" or "SCPS" => "NTSC-J",
            "SCES" or "SLES" => "PAL",
            "SCJS" or "SLJS" => "NTSC-J",
            _ => "Unknown"
        };
        }

        private static void FindArt(string rootPath, LibraryGame game)
        {
            string artDir = Path.Combine(rootPath, "ART");
            if (!Directory.Exists(artDir)) return;

            string gameId = game.GameId;
            string gameIdAlt = gameId.Replace('-', '_').Replace(".", "");

            string[] exts = { ".jpg", ".png", ".bmp" };
            foreach (var id in new[] { gameId, gameIdAlt })
            {
                if (string.IsNullOrEmpty(game.CoverPath))
                {
                    foreach (var ext in exts)
                    {
                        string path = Path.Combine(artDir, $"{id}_COV{ext}");
                        if (File.Exists(path)) { game.CoverPath = path; break; }
                    }
                }

                if (string.IsNullOrEmpty(game.IconPath))
                {
                    foreach (var ext in exts)
                    {
                        string path = Path.Combine(artDir, $"{id}_ICO{ext}");
                        if (File.Exists(path)) { game.IconPath = path; break; }
                    }
                }

                if (string.IsNullOrEmpty(game.ScreenshotPath))
                {
                    foreach (var ext in exts)
                    {
                        string path = Path.Combine(artDir, $"{id}_SCR{ext}");
                        if (File.Exists(path)) { game.ScreenshotPath = path; break; }
                    }
                }

                if (string.IsNullOrEmpty(game.BackgroundPath))
                {
                    foreach (var ext in exts)
                    {
                        string path = Path.Combine(artDir, $"{id}_BG{ext}");
                        if (File.Exists(path)) { game.BackgroundPath = path; break; }
                    }
                }
            }

            // Collect multiple screenshots
            var shots = new List<string>();
            if (!string.IsNullOrEmpty(game.ScreenshotPath))
                shots.Add(game.ScreenshotPath);

            foreach (var id in new[] { gameId, gameIdAlt })
            {
                for (int i = 2; i <= 4; i++)
                {
                    foreach (var ext in exts)
                    {
                        string path = Path.Combine(artDir, $"{id}_SCR{i}{ext}");
                        if (File.Exists(path) && !shots.Contains(path))
                        {
                            shots.Add(path);
                            break;
                        }
                    }
                }
            }
            game.ScreenshotPaths = shots.ToArray();
        }

        public static bool IsOplRoot(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;

            foreach (var folder in OplFolders)
            {
                if (Directory.Exists(Path.Combine(path, folder)))
                    return true;
            }

            return File.Exists(Path.Combine(path, "ul.cfg"));
        }

        public static void CreateOplStructure(string rootPath)
        {
            foreach (var folder in OplFolders.Concat(OplSupportFolders))
            {
                string path = Path.Combine(rootPath, folder);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
        }

        public static async Task<(int artCount, int screensDownloaded, int screensSkipped)> DownloadArtForGameAsync(
            string gameId, string artDir, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(artDir))
                return (0, 0, 0);

            if (!Directory.Exists(artDir))
                Directory.CreateDirectory(artDir);

            await OPLService.DownloadArtAsync(gameId, artDir, progress, ct);

            var scr = await OPLService.DownloadScreenshotsForGameAsync(gameId, artDir, 15, null, ct);

            return (3, scr.downloaded, scr.skipped);
        }

        public static async Task DownloadArtForAllAsync(IEnumerable<LibraryGame> games, string rootPath, IProgress<(string gameId, int current, int total, double percent)>? progress = null, CancellationToken ct = default)
        {
            string artDir = Path.Combine(rootPath, "ART");
            if (!Directory.Exists(artDir))
                Directory.CreateDirectory(artDir);

            var gameList = games.ToList();
            int total = gameList.Count;

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var game = gameList[i];

                if (!string.IsNullOrEmpty(game.CoverPath) && !string.IsNullOrEmpty(game.IconPath))
                    continue;

                try
                {
                    double percent = (double)(i + 1) / total * 100;
                    progress?.Report((game.GameId, i + 1, total, percent));

                    await OPLService.DownloadArtAsync(game.GameId, artDir, null, ct);
                }
                catch { }
            }
        }

        public static async Task<(int downloaded, int skipped, int failed, string lastError)> DownloadLogosForAllAsync(
            IEnumerable<LibraryGame> games, string rootPath,
            IProgress<(string gameId, int current, int total, double percent)>? progress = null,
            CancellationToken ct = default)
        {
            string artDir = Path.Combine(rootPath, "ART");
            if (!Directory.Exists(artDir))
                Directory.CreateDirectory(artDir);

            var gameList = games.ToList();
            int total = gameList.Count;
            int downloaded = 0, skipped = 0, failed = 0;
            string lastError = "";

            using var semaphore = new SemaphoreSlim(6);
            var tasks = new List<Task>();

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var game = gameList[i];
                int idx = i + 1;

                // Skip if logo already exists at any conventional name
                string[] variants = { ".png", ".PNG", ".jpg", ".JPG", ".jpeg", ".JPEG" };
                bool alreadyHas = variants.Any(ext =>
                    File.Exists(Path.Combine(artDir, $"{game.GameId}_LGO{ext}")));

                if (alreadyHas)
                {
                    skipped++;
                    progress?.Report((game.GameId, idx, total, (double)idx / total * 100));
                    continue;
                }

                await semaphore.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string url = $"https://raw.githubusercontent.com/Luden02/psx-ps2-opl-art-database/refs/heads/main/PS2/{game.GameId}/{game.GameId}_LGO.png";
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(15);

                        var data = await client.GetByteArrayAsync(url, cts.Token);
                        await File.WriteAllBytesAsync(
                            Path.Combine(artDir, $"{game.GameId}_LGO.png"), data, ct);

                        System.Threading.Interlocked.Increment(ref downloaded);
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.Increment(ref failed);
                        System.Threading.Interlocked.Exchange(ref lastError, ex.Message);
                    }
                    finally
                    {
                        semaphore.Release();
                        progress?.Report((game.GameId, idx, total, (double)idx / total * 100));
                    }
                }, ct));
            }

            try { await Task.WhenAll(tasks); } catch { }
            return (downloaded, skipped, failed, lastError);
        }

        public static List<string> DetectInvalidFiles(string rootPath)
        {
            var invalid = new List<string>();

            foreach (var folder in OplFolders)
            {
                string folderPath = Path.Combine(rootPath, folder);
                if (!Directory.Exists(folderPath)) continue;

                try
                {
                    var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string gameId = ExtractGameId(file);
                        if (gameId == Path.GetFileNameWithoutExtension(file))
                        {
                            // Couldn't extract a proper game ID
                            invalid.Add(file);
                        }
                    }
                }
                catch { }
            }

            return invalid;
        }

        public static async Task RenameToConventionAsync(string rootPath, string convention, IProgress<(string file, int current, int total)>? progress = null, CancellationToken ct = default)
        {
            await Task.Run(() =>
            {
                var files = new List<string>();
                foreach (var folder in new[] { "CD", "DVD" })
                {
                    string folderPath = Path.Combine(rootPath, folder);
                    if (!Directory.Exists(folderPath)) continue;
                    files.AddRange(Directory.GetFiles(folderPath, "*.iso", SearchOption.AllDirectories));
                    files.AddRange(Directory.GetFiles(folderPath, "*.zso", SearchOption.AllDirectories));
                }

                int total = files.Count;
                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string file = files[i];
                    progress?.Report((file, i + 1, total));

                    try
                    {
                        string gameId = ExtractGameId(file);
                        if (string.IsNullOrEmpty(gameId) || gameId == Path.GetFileNameWithoutExtension(file))
                            continue;

                        string dir = Path.GetDirectoryName(file) ?? "";
                        string ext = Path.GetExtension(file);
                        string newFileName = convention == "new"
                            ? $"{gameId}{ext}"
                            : $"{Path.GetFileNameWithoutExtension(file)}{ext}";

                        if (convention == "old")
                        {
                            string name = ExtractGameName(file, gameId);
                            newFileName = $"{gameId}.{name}{ext}";
                        }

                        string newPath = Path.Combine(dir, newFileName);
                        if (file != newPath && !File.Exists(newPath))
                            File.Move(file, newPath);
                    }
                    catch { }
                }
            }, ct);
        }
    }
}
