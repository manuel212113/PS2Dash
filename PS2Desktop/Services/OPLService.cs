using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace PS2Desktop.Services
{
    public class OPLService
    {
        private const long GB = 1073741824;
        private const int WR_SIZE = 524288;
        private static readonly HttpClient _http = new HttpClient();

        private static readonly uint[] CrcTable = GenerateCrcTable();

        private static uint[] GenerateCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                }
                table[i] = crc;
            }
            return table;
        }

        public static uint Crc32(string str)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(str);
            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
            }
            return crc ^ 0xFFFFFFFF;
        }

        public struct GameInfo
        {
            public string Name;
            public string GameId;
            public int Parts;
            public byte Media;
            public string MediaLabel;
            public long FileSize;
        }

        public struct ConversionProgress
        {
            public int CurrentPart;
            public int TotalParts;
            public long BytesWritten;
            public long TotalBytes;
            public double PercentComplete;
            public string StatusMessage;
        }

        public static string GetPartFilePath(string drive, string gameName, string gameId, int partNumber)
        {
            uint crc = Crc32(gameName);
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
            return Path.Combine(drivePath, $"ul.{crc:X8}.{gameId}.{partNumber:D2}");
        }

        public static string GetCfgFilePath(string drive)
        {
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
            return Path.Combine(drivePath, "ul.cfg");
        }

        public static void WriteUlCfgEntry(string drive, string gameName, string gameId, byte media, int parts)
        {
            string cfgPath = GetCfgFilePath(drive);

            using (var fs = new FileStream(cfgPath, FileMode.Append, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                byte[] nameBytes = new byte[32];
                byte[] nameSrc = Encoding.ASCII.GetBytes(gameName);
                Array.Copy(nameSrc, nameBytes, Math.Min(nameSrc.Length, 32));

                byte[] imageBytes = new byte[15];
                string imageStr = "ul." + gameId;
                byte[] imageSrc = Encoding.ASCII.GetBytes(imageStr);
                Array.Copy(imageSrc, imageBytes, Math.Min(imageSrc.Length, 15));

                byte[] pad = new byte[15];
                pad[4] = 0x08;

                bw.Write(nameBytes);
                bw.Write(imageBytes);
                bw.Write((byte)parts);
                bw.Write(media);
                bw.Write(pad);
            }
        }

        public static bool CheckGameExists(string drive, string gameName, string gameId)
        {
            string cfgPath = GetCfgFilePath(drive);
            if (!File.Exists(cfgPath)) return false;

            using (var fs = new FileStream(cfgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                while (fs.Position + 64 <= fs.Length)
                {
                    byte[] entry = br.ReadBytes(64);
                    if (entry.Length < 64) break;

                    string name = Encoding.ASCII.GetString(entry, 0, 32).TrimEnd('\0');
                    string image = Encoding.ASCII.GetString(entry, 32, 15).TrimEnd('\0');

                    if (name == gameName || image == "ul." + gameId)
                        return true;
                }
            }
            return false;
        }

        public struct UlGameEntry
        {
            public string Name;
            public string GameId;
            public int Parts;
            public byte Media;
            public long SizeBytes;
        }

        public static UlGameEntry[] ReadUlCfg(string drive)
        {
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
            string cfgPath = GetCfgFilePath(drive);

            Debug.WriteLine($"[UL] drive={drive} drivePath={drivePath} cfgPath={cfgPath}");
            Debug.WriteLine($"[UL] cfg exists: {File.Exists(cfgPath)}");

            if (!File.Exists(cfgPath)) return Array.Empty<UlGameEntry>();

            var entries = new System.Collections.Generic.List<UlGameEntry>();

            using (var fs = new FileStream(cfgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                // Read all ul.* files in root directory only (matches OrbitOPL approach)
                string[] allUlFiles;
                try { allUlFiles = Directory.GetFiles(drivePath, "ul.*", SearchOption.TopDirectoryOnly); }
                catch (Exception ex) { Debug.WriteLine($"[UL] GetFiles error: {ex.Message}"); allUlFiles = Array.Empty<string>(); }

                Debug.WriteLine($"[UL] Found {allUlFiles.Length} ul.* files in {drivePath}");
                foreach (var f in allUlFiles.Take(5))
                    Debug.WriteLine($"[UL]   {Path.GetFileName(f)}");

                while (fs.Position + 64 <= fs.Length)
                {
                    byte[] entry = br.ReadBytes(64);
                    if (entry.Length < 64) break;

                    string name = Encoding.ASCII.GetString(entry, 0, 32).TrimEnd('\0');
                    string gameIdRaw = Encoding.ASCII.GetString(entry, 32, 15).TrimEnd('\0');
                    byte parts = entry[47];
                    byte media = entry[48];

                    if (string.IsNullOrEmpty(name)) continue;

                    // Normalize gameId: strip "ul." prefix, dots, hyphens → XXXX_NNNNN
                    string gameIdClean = gameIdRaw
                        .Replace("ul.", "").Replace("ul_", "").Replace("ul-", "")
                        .Replace(".", "").Replace("-", "").Replace("_", "")
                        .ToUpperInvariant();
                    // Reformat to XXXX_###.## if we have enough chars
                    string gameIdNorm = gameIdClean.Length >= 9
                        ? $"{gameIdClean.Substring(0, 4)}_{gameIdClean.Substring(4, 3)}.{gameIdClean.Substring(7, 2)}"
                        : gameIdClean;

                    long totalSize = 0;
                    int foundParts = 0;

                    if (parts > 0)
                    {
                        // 1) Match by CRC32(name) only — OPL format: ul.{CRC}.{GAMEID}.XX
                        uint crc = Crc32(name);
                        string prefixCrc = $"ul.{crc:X8}.";

                        Debug.WriteLine($"[UL] name=\"{name}\" crc={crc:X8} prefixCrc={prefixCrc} parts={parts}");

                        foreach (var filePath in allUlFiles)
                        {
                            string upperName = Path.GetFileName(filePath).ToUpperInvariant();
                            if (upperName.StartsWith(prefixCrc))
                            {
                                try
                                {
                                    totalSize += new FileInfo(filePath).Length;
                                    foundParts++;
                                }
                                catch { }
                            }
                        }

                        // 2) Fallback: match by gameId appearing anywhere in filename
                        //    OPL format: ul.{CRC}.{GAMEID}.{PART}
                        if (foundParts == 0)
                        {
                            string gameIdUpper = gameIdRaw.Replace("ul.", "").Replace("ul_", "").Replace("ul-", "").Trim().ToUpperInvariant();
                            Debug.WriteLine($"[UL] fallback: searching for gameId \"{gameIdUpper}\" in filenames");
                            foreach (var filePath in allUlFiles)
                            {
                                string upperName = Path.GetFileName(filePath).ToUpperInvariant();
                                if (upperName.Contains($".{gameIdUpper}.") || upperName.Contains($".{gameIdUpper.Replace(".", "")}."))
                                {
                                    try
                                    {
                                        totalSize += new FileInfo(filePath).Length;
                                        foundParts++;
                                    }
                                    catch { }
                                }
                            }
                        }
                    }

                    Debug.WriteLine($"[UL] => {name}: foundParts={foundParts} totalSize={totalSize} ({FormatSize(totalSize)})");

                    entries.Add(new UlGameEntry
                    {
                        Name = name,
                        GameId = gameIdNorm,
                        Parts = parts,
                        Media = media,
                        SizeBytes = totalSize
                    });
                }
            }
            Debug.WriteLine($"[UL] Total entries: {entries.Count}, with size: {entries.Count(e => e.SizeBytes > 0)}");
            return entries.ToArray();
        }

        public static void DeleteGame(string drive, string gameName, string gameId, int parts)
        {
            uint crc = Crc32(gameName);
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
            string fileName0 = $"ul.{crc:X8}.{gameId}.00";
            string partDir = FindDirectoryContainingFile(drivePath, fileName0) ?? drivePath;

            for (int i = 0; i < parts; i++)
            {
                string partPath = Path.Combine(partDir, $"ul.{crc:X8}.{gameId}.{i:D2}");
                if (File.Exists(partPath)) File.Delete(partPath);
            }

            RemoveFromUlCfg(drive, gameName, gameId);
        }

        public static void RenameGame(string drive, string oldName, string newName, string gameId, int parts)
        {
            uint oldCrc = Crc32(oldName);
            uint newCrc = Crc32(newName);
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
            string fileName0 = $"ul.{oldCrc:X8}.{gameId}.00";
            string partDir = FindDirectoryContainingFile(drivePath, fileName0) ?? drivePath;

            for (int i = 0; i < parts; i++)
            {
                string oldPath = Path.Combine(partDir, $"ul.{oldCrc:X8}.{gameId}.{i:D2}");
                string newPath = Path.Combine(partDir, $"ul.{newCrc:X8}.{gameId}.{i:D2}");
                if (File.Exists(oldPath) && oldPath != newPath)
                    File.Move(oldPath, newPath);
            }

            UpdateNameInUlCfg(drive, oldName, newName, gameId);
        }

        private static void RemoveFromUlCfg(string drive, string gameName, string gameId)
        {
            string cfgPath = GetCfgFilePath(drive);
            if (!File.Exists(cfgPath)) return;

            var allEntries = new System.Collections.Generic.List<byte[]>();
            using (var fs = new FileStream(cfgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                while (fs.Position + 64 <= fs.Length)
                {
                    byte[] entry = br.ReadBytes(64);
                    if (entry.Length < 64) break;
                    allEntries.Add(entry);
                }
            }

            using (var fs = new FileStream(cfgPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                foreach (var entry in allEntries)
                {
                    string name = Encoding.ASCII.GetString(entry, 0, 32).TrimEnd('\0');
                    string image = Encoding.ASCII.GetString(entry, 32, 15).TrimEnd('\0');

                    if (name != gameName && image != "ul." + gameId)
                        bw.Write(entry);
                }
            }
        }

        private static void UpdateNameInUlCfg(string drive, string oldName, string newName, string gameId)
        {
            string cfgPath = GetCfgFilePath(drive);
            if (!File.Exists(cfgPath)) return;

            var allEntries = new System.Collections.Generic.List<byte[]>();
            using (var fs = new FileStream(cfgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                while (fs.Position + 64 <= fs.Length)
                {
                    byte[] entry = br.ReadBytes(64);
                    if (entry.Length < 64) break;
                    allEntries.Add(entry);
                }
            }

            using (var fs = new FileStream(cfgPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                foreach (var entry in allEntries)
                {
                    string name = Encoding.ASCII.GetString(entry, 0, 32).TrimEnd('\0');
                    string image = Encoding.ASCII.GetString(entry, 32, 15).TrimEnd('\0');

                    if (name == oldName && image == "ul." + gameId)
                    {
                        byte[] nameSrc = Encoding.ASCII.GetBytes(newName);
                        byte[] nameBytes = new byte[32];
                        Array.Copy(nameSrc, nameBytes, Math.Min(nameSrc.Length, 32));
                        Array.Copy(nameBytes, 0, entry, 0, 32);
                    }
                    bw.Write(entry);
                }
            }
        }

        public static async Task ConvertIsoToUlAsync(
            string isoPath,
            string outputDrive,
            string gameName,
            string gameId,
            IProgress<ConversionProgress> progress,
            System.Threading.CancellationToken ct = default)
        {
            var info = new FileInfo(isoPath);
            long fileSize = info.Length;
            int totalParts = (int)((fileSize + GB - 1) / GB);

            using (var isoStream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long isoPos = 0;

                for (int part = 0; part < totalParts; part++)
                {
                    ct.ThrowIfCancellationRequested();

                    string partPath = GetPartFilePath(outputDrive, gameName, gameId, part);

                    using (var partStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        long remaining = fileSize - isoPos;
                        long partSize = Math.Min(remaining, GB);
                        long partPos = 0;

                        byte[] buf = new byte[WR_SIZE];
                        while (partPos < partSize)
                        {
                            ct.ThrowIfCancellationRequested();

                            int toRead = (int)Math.Min(WR_SIZE, partSize - partPos);
                            int read = isoStream.Read(buf, 0, toRead);
                            if (read == 0) break;

                            partStream.Write(buf, 0, read);
                            isoPos += read;
                            partPos += read;

                            progress?.Report(new ConversionProgress
                            {
                                CurrentPart = part + 1,
                                TotalParts = totalParts,
                                BytesWritten = isoPos,
                                TotalBytes = fileSize,
                                PercentComplete = (double)isoPos / fileSize * 100,
                                StatusMessage = $"Parte {part + 1}/{totalParts} - {FormatSize(isoPos)}/{FormatSize(fileSize)}"
                            });
                        }
                    }
                }
            }

            byte media = 0x14;
            string ext = Path.GetExtension(isoPath).ToUpper();
            if (ext == ".ISO")
            {
                using (var iso = new ISOReader())
                {
                    iso.Init(isoPath);
                    if (iso.IsCD()) media = 0x12;
                }
            }

            WriteUlCfgEntry(outputDrive, gameName, gameId, media, totalParts);

            progress?.Report(new ConversionProgress
            {
                CurrentPart = totalParts,
                TotalParts = totalParts,
                BytesWritten = fileSize,
                TotalBytes = fileSize,
                PercentComplete = 100,
                StatusMessage = "Conversión completada"
            });
        }

        public static async Task ConvertBinToIsoAsync(
            string binPath,
            string outputPath,
            IProgress<ConversionProgress> progress,
            System.Threading.CancellationToken ct = default)
        {
            var info = new FileInfo(binPath);
            long fileSize = info.Length;

            using (var binStream = new FileStream(binPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var isoStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long copied = 0;
                byte[] buf = new byte[WR_SIZE];

                while (copied < fileSize)
                {
                    ct.ThrowIfCancellationRequested();

                    int toRead = (int)Math.Min(WR_SIZE, fileSize - copied);
                    int read = binStream.Read(buf, 0, toRead);
                    if (read == 0) break;

                    isoStream.Write(buf, 0, read);
                    copied += read;

                    progress?.Report(new ConversionProgress
                    {
                        CurrentPart = 1,
                        TotalParts = 1,
                        BytesWritten = copied,
                        TotalBytes = fileSize,
                        PercentComplete = (double)copied / fileSize * 100,
                        StatusMessage = $"Convirtiendo... {FormatSize(copied)}/{FormatSize(fileSize)}"
                    });
                }
            }

            progress?.Report(new ConversionProgress
            {
                CurrentPart = 1,
                TotalParts = 1,
                BytesWritten = fileSize,
                TotalBytes = fileSize,
                PercentComplete = 100,
                StatusMessage = "Conversión completada"
            });
        }

        public static async Task DownloadArtAsync(
            string gameId,
            string outputDir,
            IProgress<double> progress,
            System.Threading.CancellationToken ct = default)
        {
            string normalizedId = gameId.Replace("-", "_");
            int dotPos = normalizedId.IndexOf('.');
            if (dotPos > 0)
                normalizedId = normalizedId.Substring(0, dotPos) + "." + normalizedId.Substring(dotPos + 1).PadLeft(2, '0');

            string[] artTypes = { "COV", "ICO", "BG" };
            string[] artUrls =
            {
                $"https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/{normalizedId}.jpg",
                $"https://raw.githubusercontent.com/HowlingWolfHWC/PS2-Art-and-Info-for-OPL-and-XEBPlus/main/ART/{normalizedId}_ICO.png",
                $"https://raw.githubusercontent.com/Luden02/psx-ps2-opl-art-database/main/PS2/{normalizedId}/BG.PNG"
            };

            Directory.CreateDirectory(outputDir);

            for (int i = 0; i < artUrls.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    byte[] data = await _http.GetByteArrayAsync(artUrls[i]);
                    string ext = artUrls[i].EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
                    string filePath = Path.Combine(outputDir, $"{normalizedId}_{artTypes[i]}{ext}");
                    await File.WriteAllBytesAsync(filePath, data);

                    progress?.Report((double)(i + 1) / artUrls.Length * 100);
                }
                catch
                {
                    progress?.Report((double)(i + 1) / artUrls.Length * 100);
                }
            }
        }

        public static async Task<(int downloaded, int skipped)> DownloadScreenshotsForGameAsync(
            string gameId, string outputDir, int maxIndex = 15,
            IProgress<int>? progress = null,
            System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(outputDir))
                return (0, 0);

            Directory.CreateDirectory(outputDir);

            int downloaded = 0;
            int skipped = 0;
            int consecutiveMisses = 0;

            for (int i = 0; i <= maxIndex; i++)
            {
                ct.ThrowIfCancellationRequested();

                string fileName = $"{gameId}_SCR_{i:D2}.png";
                string filePath = Path.Combine(outputDir, fileName);

                if (File.Exists(filePath))
                {
                    skipped++;
                    progress?.Report(i);
                    continue;
                }

                string url = $"https://raw.githubusercontent.com/Luden02/psx-ps2-opl-art-database/refs/heads/main/PS2/{gameId}/{fileName}";

                try
                {
                    var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));

                    var data = await _http.GetByteArrayAsync(url, cts.Token);
                    await File.WriteAllBytesAsync(filePath, data, ct);

                    downloaded++;
                    consecutiveMisses = 0;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    consecutiveMisses++;
                    if (consecutiveMisses >= 2) break;
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    consecutiveMisses++;
                    if (consecutiveMisses >= 2) break;
                }
                catch
                {
                    consecutiveMisses++;
                    if (consecutiveMisses >= 2) break;
                }

                progress?.Report(i);
            }

            return (downloaded, skipped);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= GB) return $"{bytes / (double)GB:F1} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }

        private static string FindDirectoryContainingFile(string rootDir, string fileName)
        {
            try
            {
                if (File.Exists(Path.Combine(rootDir, fileName))) return rootDir;
                foreach (var sub in Directory.GetDirectories(rootDir))
                {
                    if (File.Exists(Path.Combine(sub, fileName))) return sub;
                    foreach (var sub2 in Directory.GetDirectories(sub))
                    {
                        if (File.Exists(Path.Combine(sub2, fileName))) return sub2;
                    }
                }
            }
            catch { }
            return null;
        }

        public static string FindPartDirectory(string drive, string name, string gameId)
        {
            uint crc = Crc32(name);
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
            string fileName0 = $"ul.{crc:X8}.{gameId}.00";
            return FindDirectoryContainingFile(drivePath, fileName0);
        }
    }
}
