using System;
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
                uint crc = i << 24;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x80000000) != 0)
                        crc = (crc << 1) ^ 0x04C11DB7;
                    else
                        crc <<= 1;
                }
                table[255 - i] = crc;
            }
            return table;
        }

        public static uint Crc32(string str)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(str);
            uint crc = 0;
            foreach (byte b in bytes)
            {
                crc = CrcTable[b ^ ((crc >> 24) & 0xFF)] ^ ((crc << 8) & 0xFFFFFF00);
            }
            return crc;
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
            string cfgPath = GetCfgFilePath(drive);
            if (!File.Exists(cfgPath)) return Array.Empty<UlGameEntry>();

            var entries = new System.Collections.Generic.List<UlGameEntry>();

            using (var fs = new FileStream(cfgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                while (fs.Position + 64 <= fs.Length)
                {
                    byte[] entry = br.ReadBytes(64);
                    if (entry.Length < 64) break;

                    string name = Encoding.ASCII.GetString(entry, 0, 32).TrimEnd('\0');
                    string image = Encoding.ASCII.GetString(entry, 32, 15).TrimEnd('\0');
                    byte parts = entry[47];
                    byte media = entry[48];

                    if (string.IsNullOrEmpty(name)) continue;
                    string gameId = image.StartsWith("ul.") ? image.Substring(3) : "";

                    long totalSize = 0;
                    if (parts > 0 && !string.IsNullOrEmpty(gameId))
                    {
                        uint crc = Crc32(name);
                        string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";
                        for (int i = 0; i < parts; i++)
                        {
                            string partPath = Path.Combine(drivePath, $"ul.{crc:X8}.{gameId}.{i:D2}");
                            if (File.Exists(partPath))
                                totalSize += new FileInfo(partPath).Length;
                        }
                    }

                    entries.Add(new UlGameEntry
                    {
                        Name = name,
                        GameId = gameId,
                        Parts = parts,
                        Media = media,
                        SizeBytes = totalSize
                    });
                }
            }
            return entries.ToArray();
        }

        public static void DeleteGame(string drive, string gameName, string gameId, int parts)
        {
            uint crc = Crc32(gameName);
            string drivePath = drive.Length == 1 ? drive + ":\\" : drive + "\\";

            for (int i = 0; i < parts; i++)
            {
                string partPath = Path.Combine(drivePath, $"ul.{crc:X8}.{gameId}.{i:D2}");
                if (File.Exists(partPath)) File.Delete(partPath);
            }

            RemoveFromUlCfg(drive, gameName, gameId);
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

        public static string FormatSize(long bytes)
        {
            if (bytes >= GB) return $"{bytes / (double)GB:F2} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
