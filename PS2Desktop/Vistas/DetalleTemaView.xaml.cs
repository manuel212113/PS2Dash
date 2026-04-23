using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace PS2Desktop.Vistas
{
    public partial class DetalleTemaView : UserControl, IDisposable
    {
        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private bool _isDraggingSlider = false;
        private readonly YoutubeClient _youtubeClient = new YoutubeClient();

        // Lista para manejar la navegación (Video + Imágenes)
        private List<string> _mediaSources = new List<string>
        {
            "https://www.youtube.com/watch?v=1rKvzlDDiLw", // Índice 0: Video
            "https://repository-images.githubusercontent.com/70989832/bbb2a500-ca21-11ea-9e6d-8b11db4ff655", // Índice 1
            "https://repository-images.githubusercontent.com/70989832/bbb2a500-ca21-11ea-9e6d-8b11db4ff655"  // Índice 2
        };
        private int _currentIndex = 0;

        public DetalleTemaView()
        {
            Core.Initialize();
            InitializeComponent();

            this.Loaded += (s, e) => MainScrollViewer.Focus();
            _libVLC = new LibVLC();
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
            VideoPlayerDetalle.MediaPlayer = _mediaPlayer;

            // Evento para actualizar el slider y tiempo
            _mediaPlayer.TimeChanged += (s, e) =>
            {
                if (!_isDraggingSlider)
                {
                    Dispatcher.BeginInvoke(new Action(() => {
                        if (_mediaPlayer == null || TimelineSlider == null) return;
                        TimelineSlider.Value = _mediaPlayer.Position * 100;
                        TimeSpan t = TimeSpan.FromMilliseconds(e.Time);
                        lblTime.Text = $"{t.Minutes}:{t.Seconds:D2}";

                        if (_mediaPlayer.Length > 0)
                        {
                            TimeSpan total = TimeSpan.FromMilliseconds(_mediaPlayer.Length);
                            lblTotalTime.Text = $"{total.Minutes}:{total.Seconds:D2}";
                        }
                    }));
                }
            };

            this.Loaded += DetalleTemaView_Loaded;
            this.Unloaded += (s, e) => Dispose();
        }

        private async void DetalleTemaView_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(300);
            await CambiarMedia(0); // Cargar video por defecto
        }

        // Lógica para cambiar entre video e imágenes
        private async Task CambiarMedia(int index)
        {
            _currentIndex = index;
            LoadingPanel.Visibility = Visibility.Visible;
            ActualizarBordesMiniaturas(index);

            try
            {
                if (index == 0) // Es el Video de YouTube
                {
                    var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(_mediaSources[index]);
                    var streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();

                    if (streamInfo != null)
                    {
                        var media = new Media(_libVLC, new Uri(streamInfo.Url));
                        _mediaPlayer.Play(media);
                        btnPlayPause.Content = this.FindResource("IconPause");
                    }
                }
                else // Es una imagen
                {
                    var media = new Media(_libVLC, new Uri(_mediaSources[index]));
                    _mediaPlayer.Play(media);
                    // Al ser imagen, pausamos para que no intente "reproducir" nada más
                    await Task.Delay(500);
                    _mediaPlayer.Pause();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al cambiar media: " + ex.Message);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ActualizarBordesMiniaturas(int index)
        {
            // Resetear todos a 0
            BorderThumb0.BorderThickness = new Thickness(0);
            BorderThumb1.BorderThickness = new Thickness(0);
            BorderThumb2.BorderThickness = new Thickness(0);

            // Resaltar el seleccionado
            if (index == 0) BorderThumb0.BorderThickness = new Thickness(2);
            else if (index == 1) BorderThumb1.BorderThickness = new Thickness(2);
            else if (index == 2) BorderThumb2.BorderThickness = new Thickness(2);
        }

        // Evento de clic en miniaturas
        private async void SeleccionarMedia_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string tag = btn.Tag.ToString();

            if (tag == "video") await CambiarMedia(0);
            else if (tag == "img1") await CambiarMedia(1);
            else if (tag == "img2") await CambiarMedia(2);
        }

        // Navegación con flechas
        private async void btnPrev_Click(object sender, RoutedEventArgs e)
        {
            int newIndex = _currentIndex - 1;
            if (newIndex < 0) newIndex = _mediaSources.Count - 1;
            await CambiarMedia(newIndex);
        }

        private async void btnNext_Click(object sender, RoutedEventArgs e)
        {
            int newIndex = (_currentIndex + 1) % _mediaSources.Count;
            await CambiarMedia(newIndex);
        }

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
            // Podrías alternar iconos aquí si tuvieras un IconVolOff
            btnMute.Content = this.FindResource("IconVolOn");
        }

        private void TimelineSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _isDraggingSlider = true;

        private void TimelineSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _mediaPlayer.Position = (float)(TimelineSlider.Value / 100.0);
            _isDraggingSlider = false;
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void MainScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - 40);
            if (e.Key == Key.Down) MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + 40);
            e.Handled = true;
        }

        public void Dispose()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }
    }
}