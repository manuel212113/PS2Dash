using System;
using System.IO;
using System.Media;
using System.Windows.Media;

namespace PS2Desktop.Services
{
    public static class SoundService
    {
        private static readonly MediaPlayer _player = new MediaPlayer();
        private static string _clickPath;
        private static string _completePath;

        public static void Initialize()
        {
            var sonidosDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sonidos");
            _clickPath = Path.Combine(sonidosDir, "click.mp3");
            _completePath = Path.Combine(sonidosDir, "complete.mp3");
            _player.Volume = 1.0;
        }

        public static void PlayClick()
        {
            try
            {
                if (!File.Exists(_clickPath)) return;
                _player.Open(new Uri(_clickPath));
                _player.Play();
            }
            catch { }
        }

        public static void PlayDownloadComplete()
        {
            try
            {
                if (File.Exists(_completePath))
                {
                    _player.Open(new Uri(_completePath));
                    _player.Play();
                }
                else
                {
                    SystemSounds.Asterisk.Play();
                }
            }
            catch { }
        }
    }
}
