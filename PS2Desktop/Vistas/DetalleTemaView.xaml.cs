using LibVLCSharp.Shared;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PS2Desktop.Vistas
{
    public partial class DetalleTemaView : UserControl, IDisposable
    {
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private bool _isDraggingSlider = false;

        public DetalleTemaView()
        {
            Core.Initialize();
            InitializeComponent();

            // --- GESTIÓN DE FOCO PARA NAVEGACIÓN ---
            this.Loaded += (s, e) => MainScrollViewer.Focus();
            // Si haces clic en cualquier parte, el foco vuelve al ScrollViewer para que las flechas sigan funcionando
            this.PreviewMouseDown += (s, e) => MainScrollViewer.Focus();

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            VideoPlayerDetalle.MediaPlayer = _mediaPlayer;

            // Actualización del tiempo y slider
            _mediaPlayer.TimeChanged += (s, e) =>
            {
                if (!_isDraggingSlider)
                {
                    Dispatcher.BeginInvoke(new Action(() => {
                        // Verificación de nulidad para evitar errores al cerrar la vista
                        if (_mediaPlayer == null || TimelineSlider == null) return;

                        TimelineSlider.Value = _mediaPlayer.Position * 100;
                        TimeSpan t = TimeSpan.FromMilliseconds(e.Time);
                        lblTime.Text = $"{t.Minutes}:{t.Seconds:D2}";
                    }));
                }
            };

            this.Loaded += DetalleTemaView_Loaded;
            this.Unloaded += (s, e) => Dispose();
        }

        private async void DetalleTemaView_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(200);
            // Asegúrate de que esta URL sea válida o cámbiala por un recurso local
            var media = new Media(_libVLC, new Uri("http://136.248.242.116:8083/p/thema1.opl.mp4?sign=Ok2bJXy9kO0MiCe9VB40aM5XfPIjwDO53qQCzGMrYfE=:0"));
            _mediaPlayer.Play(media);
            btnPlayPause.Content = this.FindResource("IconPause");
        }

        // --- MANEJO DE SCROLL (MOUSE & TECLADO) ---

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Forzamos el scroll manual para evitar que el reproductor de video bloquee el evento
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void MainScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Sensibilidad del scroll con teclado
            double scrollStep = 40;

            if (e.Key == Key.Up)
            {
                MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - scrollStep);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + scrollStep);
                e.Handled = true;
            }
        }

        // --- CONTROLES DE VIDEO ---

        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                btnPlayPause.Content = this.FindResource("IconPlay");
            }
            else
            {
                _mediaPlayer.Play();
                btnPlayPause.Content = this.FindResource("IconPause");
            }
        }

        private void btnMute_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer.Mute = !_mediaPlayer.Mute;
            // Verifica que tengas "IconVolOff" en tus Resources o usa IconVolOn
            string iconName = _mediaPlayer.Mute ? "IconVolOn" : "IconVolOn";
            btnMute.Content = this.FindResource(iconName);
        }

        private void TimelineSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
            => _isDraggingSlider = true;

        private void TimelineSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _mediaPlayer.Position = (float)(TimelineSlider.Value / 100.0);
            _isDraggingSlider = false;
        }

        public void Dispose()
        {
            // Limpieza de recursos de VLC
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            _mediaPlayer = null;
            _libVLC = null;
        }
    }
}