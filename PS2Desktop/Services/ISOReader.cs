using System;
using System.IO;
using System.Text;

namespace PS2Desktop.Services
{
    public class ISOReader : IDisposable
    {
        private FileStream _stream;
        private long _isoSize;
        private bool _disposed;

        public long IsoSize => _isoSize;

        public long Init(string isoPath)
        {
            _stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _isoSize = _stream.Length;
            return _isoSize;
        }

        public void Reset()
        {
            _stream?.Close();
            _stream = null;
        }

        public string GetGameId()
        {
            string elfPath = ParseSystemCnf();
            if (string.IsNullOrEmpty(elfPath)) return null;

            int start = elfPath.IndexOf("SLUS", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = elfPath.IndexOf("SCES", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = elfPath.IndexOf("SCUS", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = elfPath.IndexOf("SLPS", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = elfPath.IndexOf("SCPS", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;

            string id = elfPath.Substring(start);
            int semi = id.IndexOf(';');
            if (semi > 0) id = id.Substring(0, semi);
            return id;
        }

        public string GetGameName()
        {
            string elfPath = ParseSystemCnf();
            if (string.IsNullOrEmpty(elfPath)) return null;

            string id = GetGameId();
            if (string.IsNullOrEmpty(id)) return null;

            string dir = Path.GetDirectoryName(elfPath)?.Replace('\\', '/') ?? "";
            return id;
        }

        public bool IsCD()
        {
            try
            {
                byte[] buf = new byte[2048];
                _stream.Position = 0x8000;
                if (_stream.Read(buf, 0, 2048) < 2048) return false;

                byte[] pvd = new byte[2048];
                _stream.Position = 0x8000;
                if (_stream.Read(pvd, 0, 2048) < 2048) return false;

                long totalSectors = _isoSize / 2048;
                return totalSectors <= 360000;
            }
            catch
            {
                return false;
            }
        }

        private string ParseSystemCnf()
        {
            try
            {
                byte[] rootDir = ReadPrimaryVolumeDescriptor();
                if (rootDir == null) return null;

                uint rootLBA = BitConverter.ToUInt32(rootDir, 158);
                uint rootSize = BitConverter.ToUInt32(rootDir, 162);
                byte[] rootDirRecord = ReadSector(rootLBA);

                int offset = 0;
                while (offset < rootSize && offset < 2048)
                {
                    byte recLen = rootDirRecord[offset];
                    if (recLen == 0)
                    {
                        offset = ((offset / 2048) + 1) * 2048;
                        if (offset >= rootSize) break;
                        rootDirRecord = ReadSector((uint)(rootLBA + offset / 2048));
                        offset = 0;
                        continue;
                    }

                    byte nameLen = rootDirRecord[offset + 32];
                    string name = Encoding.ASCII.GetString(rootDirRecord, offset + 33, nameLen);

                    if (name == "SYSTEM.CNF;1" || name.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase))
                    {
                        uint fileLBA = BitConverter.ToUInt32(rootDir, offset + 2);
                        uint fileSize = BitConverter.ToUInt32(rootDir, offset + 10);

                        byte[] cnfData = ReadSector(fileLBA);
                        string cnfText = Encoding.ASCII.GetString(cnfData).TrimEnd('\0');
                        return ParseBoot2Line(cnfText);
                    }

                    offset += recLen;
                }

                return ScanForSystemCnf();
            }
            catch
            {
                return null;
            }
        }

        private string ScanForSystemCnf()
        {
            long totalSectors = _isoSize / 2048;
            byte[] buf = new byte[2048];

            for (long sector = 16; sector < Math.Min(totalSectors, 100000); sector++)
            {
                _stream.Position = sector * 2048;
                if (_stream.Read(buf, 0, 2048) < 2048) break;

                string text = Encoding.ASCII.GetString(buf);
                if (text.Contains("BOOT2") && text.Contains("SYSTEM.CNF"))
                {
                    return ParseBoot2Line(text);
                }
            }
            return null;
        }

        private string ParseBoot2Line(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim('\r', '\0');
                if (trimmed.StartsWith("BOOT2", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;
                    string value = trimmed.Substring(eq + 1).Trim();
                    if (value.StartsWith("cdrom0:\\")) value = value.Substring(8);
                    else if (value.StartsWith("cdrom0:")) value = value.Substring(7);
                    return value;
                }
            }
            return null;
        }

        private byte[] ReadPrimaryVolumeDescriptor()
        {
            for (int sector = 16; sector < 32; sector++)
            {
                byte[] pvd = ReadSector((uint)sector);
                if (pvd[0] == 0x01 && Encoding.ASCII.GetString(pvd, 1, 5) == "CD001")
                {
                    return pvd;
                }
            }
            return null;
        }

        private byte[] ReadSector(uint lba)
        {
            byte[] buf = new byte[2048];
            _stream.Position = lba * 2048;
            _stream.Read(buf, 0, 2048);
            return buf;
        }

        public void ReadIsoChunk(long offset, byte[] buffer, int count)
        {
            _stream.Position = offset;
            _stream.Read(buffer, 0, count);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stream?.Dispose();
                _disposed = true;
            }
        }
    }
}
