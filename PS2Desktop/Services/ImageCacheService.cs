using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PS2Desktop.Services
{
    public class ImageCacheService
    {
        private static readonly Lazy<ImageCacheService> _instance = new(() => new());
        public static ImageCacheService Instance => _instance.Value;

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly Dictionary<string, BitmapImage> _memCache = new();
        private readonly LinkedList<string> _accessOrder = new();
        private readonly object _cacheLock = new();
        private int MaxCacheItems => AppSettings.MaxCacheItems;
        private static readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "PS2Desktop", "imgcache");

        private ImageCacheService()
        {
            Directory.CreateDirectory(_cacheDir);
        }

        public async Task<BitmapImage?> GetImageAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            lock (_cacheLock)
            {
                if (_memCache.TryGetValue(url, out var cached))
                {
                    _accessOrder.Remove(url);
                    _accessOrder.AddFirst(url);
                    return cached;
                }
            }

            var diskPath = GetDiskPath(url);
            if (File.Exists(diskPath))
            {
                try
                {
                    var bmp = LoadFromDisk(diskPath);
                    if (bmp != null)
                    {
                        AddToCache(url, bmp);
                        return bmp;
                    }
                }
                catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"[ImageCache] Error reading from disk: {ex.Message}"); }
                catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine($"[ImageCache] Access denied: {ex.Message}"); }
            }

            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(diskPath, bytes);

                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                }

                AddToCache(url, bitmap);
                return bitmap;
            }
            catch (HttpRequestException ex) { System.Diagnostics.Debug.WriteLine($"[ImageCache] HTTP error: {ex.Message}"); return null; }
            catch (TaskCanceledException) { return null; }
            catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"[ImageCache] IO error: {ex.Message}"); return null; }
        }

        public void PreWarm(string url)
        {
            CardVisualHelper.FireAndForget(() => GetImageAsync(url), "Error precargando imagen");
        }

        public void Clear()
        {
            lock (_cacheLock)
            {
                _memCache.Clear();
                _accessOrder.Clear();
            }
            try
            {
                if (Directory.Exists(_cacheDir))
                    Directory.Delete(_cacheDir, true);
                Directory.CreateDirectory(_cacheDir);
            }
            catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"[ImageCache] Error clearing disk cache: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine($"[ImageCache] Access denied clearing: {ex.Message}"); }
        }

        private void AddToCache(string url, BitmapImage bitmap)
        {
            lock (_cacheLock)
            {
                if (_memCache.Count >= MaxCacheItems)
                {
                    var oldest = _accessOrder.Last;
                    if (oldest != null)
                    {
                        _memCache.Remove(oldest.Value);
                        _accessOrder.RemoveLast();
                    }
                }

                _memCache[url] = bitmap;
                _accessOrder.Remove(url);
                _accessOrder.AddFirst(url);
            }
        }

        private string GetDiskPath(string url)
        {
            var hash = url.GetHashCode().ToString("x8") + "_" + Path.GetInvalidFileNameChars()
                .Aggregate(url, (s, c) => s.Replace(c, '_'));
            if (hash.Length > 120) hash = hash[..120];
            return Path.Combine(_cacheDir, hash + ".cache");
        }

        private static BitmapImage? LoadFromDisk(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
