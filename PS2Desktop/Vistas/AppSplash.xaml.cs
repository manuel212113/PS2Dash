using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PS2Desktop.Vistas
{
    public partial class AppSplash : Window
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private int _index;
        private readonly string[] _messages = {
            "Cargando Temas",
            "Cargando Temas.",
            "Cargando Temas..",
            "Cargando Temas..."
        };

        public AppSplash()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(12),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            ProgressFill.BeginAnimation(ScaleTransform.ScaleXProperty, anim);

            _timer.Interval = TimeSpan.FromMilliseconds(400);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12.2) };
            closeTimer.Tick += (s, _) =>
            {
                closeTimer.Stop();
                _timer.Stop();
                SplashVideo.Stop();
                var main = new MainWindow();
                main.Show();
                Close();
            };
            closeTimer.Start();
        }

        private void SplashVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            SplashVideo.Play();
        }

        private void SplashVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            SplashVideo.Position = TimeSpan.Zero;
            SplashVideo.Play();
        }

        private void BtnMute_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SplashVideo.IsMuted = !SplashVideo.IsMuted;
            MuteIcon.Text = SplashVideo.IsMuted ? "🔇" : "🔊";
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _index = (_index + 1) % _messages.Length;
            LblLoading.Text = _messages[_index];
        }
    }
}
