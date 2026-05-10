using System;
using System.IO;
using System.Windows.Media;

namespace PS2Desktop.Services
{
    public static class SoundService
    {
        private static readonly MediaPlayer _player = new MediaPlayer();
        private static string _clickPath;

        public static void Initialize()
        {
            _clickPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sonidos", "click.mp3");
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
    }
}
