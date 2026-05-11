using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System.Diagnostics;

namespace PS2Desktop.Vistas
{
    public partial class DetalleTemaView : UserControl, IDisposable
    {
        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private bool _isDraggingSlider = false;
        private readonly YoutubeClient _youtubeClient = new YoutubeClient();
        private readonly IVoteRepository _voteRepo;
        private readonly ISessionService _session;
        private Theme _temaActual;
        private int _userVote = 0;

        private const string DefaultVideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

        private List<string> _mediaSources = new List<string>();
        private int _currentIndex = 0;
        private DispatcherTimer _hideTimer;

        public DetalleTemaView()
        {
            Core.Initialize();
            InitializeComponent();

            _voteRepo = App.ServiceProvider.GetRequiredService<IVoteRepository>();
            _session = App.ServiceProvider.GetRequiredService<ISessionService>();

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

            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _hideTimer.Tick += (s, e) =>
            {
                if (ControlsPanel == null) return;
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5));
                fade.Completed += (_, _) =>
                {
                    if (ControlsPanel != null)
                        ControlsPanel.Visibility = Visibility.Collapsed;
                };
                ControlsPanel.BeginAnimation(UIElement.OpacityProperty, fade);
                _hideTimer.Stop();
            };

            this.Loaded += DetalleTemaView_Loaded;
            this.Unloaded += (s, e) => Dispose();
        }

        private void PlayerBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_currentIndex == 0 && ControlsPanel != null && ControlsPanel.Visibility != Visibility.Visible)
            {
                ControlsPanel.Visibility = Visibility.Visible;
                ControlsPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
                _hideTimer.Stop();
                _hideTimer.Start();
            }
        }

        private void PlayerBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (_currentIndex == 0 && ControlsPanel != null)
            {
                bool wasHidden = ControlsPanel.Visibility != Visibility.Visible;
                ControlsPanel.Visibility = Visibility.Visible;
                if (wasHidden)
                    ControlsPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
                _hideTimer.Stop();
                _hideTimer.Start();
            }
        }

        private void PlayerBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            // Timer handles auto-hide
        }

        private void ControlsPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_currentIndex == 0 && ControlsPanel != null)
            {
                ControlsPanel.Visibility = Visibility.Visible;
                _hideTimer.Stop();
                _hideTimer.Start();
            }
        }

        private void ControlsPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_currentIndex == 0 && ControlsPanel != null)
            {
                bool wasHidden = ControlsPanel.Visibility != Visibility.Visible;
                ControlsPanel.Visibility = Visibility.Visible;
                if (wasHidden)
                    ControlsPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
                _hideTimer.Stop();
                _hideTimer.Start();
            }
        }

        /// <summary>
        /// Establece el tema a mostrar en la vista de detalle
        /// </summary>
        public void SetTema(Theme tema)
        {
            _temaActual = tema;
            this.DataContext = tema;

            // --- Título ---
            lblTitle.Text = tema.nombre ?? "Sin título";

            // --- Descripción ---
            lblDescripcion.Text = tema.descripcion ?? "Sin descripción";

            // --- Características ---
            if (tema.caracteristicas != null && tema.caracteristicas.Count > 0)
            {
                CaracteristicasList.ItemsSource = tema.caracteristicas;
                CaracteristicasList.Visibility = Visibility.Visible;
            }
            else
            {
                CaracteristicasList.Visibility = Visibility.Collapsed;
            }

            // --- Rating ---
            _ = CargarRatingAsync(tema.id);

            // --- Sidebar (precio / botón) ---
            lblPrice.Text = "GRATIS";
            btnDownload.Content = string.IsNullOrEmpty(tema.link_descarga) ? "No disponible" : "CONSEGUIR";
            btnDownload.IsEnabled = !string.IsNullOrEmpty(tema.link_descarga);

            // --- Thumbnails ---
            if (!string.IsNullOrEmpty(tema.image_url))
            {
                ThumbImg0.Source = new BitmapImage(new Uri(tema.image_url, UriKind.Absolute));
                ThumbImg1.Source = new BitmapImage(new Uri(tema.image_url, UriKind.Absolute));
                ThumbImg2.Source = new BitmapImage(new Uri(tema.image_url, UriKind.Absolute));
            }
            else
            {
                ThumbImg1.Visibility = Visibility.Collapsed;
                ThumbImg2.Visibility = Visibility.Collapsed;
            }

            // --- Votación ---
            VotePanel.Visibility = Visibility.Visible;
            ConfigurarEstrellas();

            // --- Media sources ---
            _mediaSources = new List<string>
            {
                tema.video_demo,
                tema.image_url,
                null
            };
            _currentIndex = 0;
        }

        private async Task CargarRatingAsync(Guid themeId)
        {
            try
            {
                var (avg, cnt) = await _voteRepo.GetAverageRatingAsync(themeId, "theme");
                if (cnt > 0)
                {
                    RatingPanel.Visibility = Visibility.Visible;
                    int fullStars = (int)Math.Round(avg, MidpointRounding.AwayFromZero);
                    string stars = new string('★', fullStars).PadRight(5, '☆');
                    lblStars.Text = stars;
                    lblRatingValue.Text = avg.ToString("F1");
                    lblRatingCount.Text = $"({cnt} {(cnt == 1 ? "voto" : "votos")})";
                }
            }
            catch { }
        }

        private async void DetalleTemaView_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(300);
            if (_mediaSources.Count > 0 && _mediaSources[0] != null)
                await CambiarMedia(0);
        }

        private void MostrarControles(bool visible)
        {
            if (ControlsPanel == null) return;
            _hideTimer.Stop();
            ControlsPanel.BeginAnimation(UIElement.OpacityProperty, null);

            if (visible)
            {
                ControlsPanel.Visibility = Visibility.Visible;
                ControlsPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
                _hideTimer.Start();
            }
            else
            {
                var animOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
                animOut.Completed += (s, e) =>
                {
                    if (ControlsPanel != null && ControlsPanel.Opacity == 0)
                        ControlsPanel.Visibility = Visibility.Collapsed;
                };
                ControlsPanel.BeginAnimation(UIElement.OpacityProperty, animOut);
            }
        }

        private async Task CambiarMedia(int index)
        {
            _currentIndex = index;
            LoadingPanel.Visibility = Visibility.Visible;
            ActualizarBordesMiniaturas(index);

            try
            {
                if (index >= _mediaSources.Count || _mediaSources[index] == null)
                {
                    _mediaPlayer.Stop();
                    return;
                }

                bool esVideo = index == 0;
                MostrarControles(esVideo);

                if (esVideo)
                {
                    await ReproducirVideo(_mediaSources[index]);
                }
                else
                {
                    var media = new Media(_libVLC, new Uri(_mediaSources[index]));
                    _mediaPlayer.Play(media);
                    await Task.Delay(500);
                    _mediaPlayer.Pause();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error al cambiar media: " + ex.Message);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task ReproducirVideo(string videoUrl)
        {
            try
            {
                var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoUrl);
                var streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();

                if (streamInfo != null)
                {
                    var media = new Media(_libVLC, new Uri(streamInfo.Url));
                    _mediaPlayer.Play(media);
                    btnPlayPause.Content = this.FindResource("IconPause");
                    return;
                }
            }
            catch
            {
                Debug.WriteLine("Fallo el video principal, usando fallback");
            }

            if (videoUrl != DefaultVideoUrl)
            {
                try
                {
                    var fallbackManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(DefaultVideoUrl);
                    var fallbackStream = fallbackManifest.GetMuxedStreams().GetWithHighestVideoQuality();

                    if (fallbackStream != null)
                    {
                        _mediaSources[0] = DefaultVideoUrl;
                        var media = new Media(_libVLC, new Uri(fallbackStream.Url));
                        _mediaPlayer.Play(media);
                        btnPlayPause.Content = this.FindResource("IconPause");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error en fallback de video: " + ex.Message);
                }
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

            int index = tag switch
            {
                "video" => 0,
                "img1" => 1,
                "img2" => 2,
                _ => 0
            };

            if (index < _mediaSources.Count && _mediaSources[index] != null)
                await CambiarMedia(index);
        }

        // Navegación con flechas
        private async void btnPrev_Click(object sender, RoutedEventArgs e)
        {
            int newIndex = _currentIndex;
            for (int i = 0; i < _mediaSources.Count; i++)
            {
                newIndex--;
                if (newIndex < 0) newIndex = _mediaSources.Count - 1;
                if (newIndex < _mediaSources.Count && _mediaSources[newIndex] != null)
                    break;
            }
            await CambiarMedia(newIndex);
        }

        private async void btnNext_Click(object sender, RoutedEventArgs e)
        {
            int newIndex = _currentIndex;
            for (int i = 0; i < _mediaSources.Count; i++)
            {
                newIndex = (newIndex + 1) % _mediaSources.Count;
                if (_mediaSources[newIndex] != null)
                    break;
            }
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
            double newOffset = MainScrollViewer.VerticalOffset - e.Delta;
            newOffset = Math.Max(0, Math.Min(newOffset, MainScrollViewer.ScrollableHeight));
            MainScrollViewer.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        private void MainScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            double offset = MainScrollViewer.VerticalOffset;
            if (e.Key == Key.Up) offset = Math.Max(0, offset - 40);
            if (e.Key == Key.Down) offset = Math.Min(MainScrollViewer.ScrollableHeight, offset + 40);
            MainScrollViewer.ScrollToVerticalOffset(offset);
            e.Handled = true;
        }

        public void Dispose()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            _mediaPlayer = null;
            _libVLC = null;
        }

        // Aplicar un Theme al detalle
        public async Task ApplyTheme(Theme theme)
        {
            if (theme == null) return;
            SetTema(theme);
            await CambiarMedia(0);
        }

        private void ConfigurarEstrellas()
        {
            var stars = new[] { StarBtn1, StarBtn2, StarBtn3, StarBtn4, StarBtn5 };

            for (int i = 0; i < stars.Length; i++)
            {
                int idx = i + 1;
                var btn = stars[i];

                btn.MouseEnter += (s, e) =>
                {
                    for (int j = 0; j < stars.Length; j++)
                    {
                        stars[j].Content = j < idx ? "★" : "☆";
                        stars[j].Foreground = j < idx
                            ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                            : new SolidColorBrush(Color.FromRgb(136, 142, 158));
                    }
                };

                btn.MouseLeave += (s, e) => MostrarEstrellas(_userVote);

                btn.Tag = idx.ToString();
                btn.Click += (s, e) =>
                {
                    _userVote = idx;
                    MostrarEstrellas(idx);
                    _ = EnviarVotoAsync(idx);
                };
            }
        }

        private void MostrarEstrellas(int hasta)
        {
            var stars = new[] { StarBtn1, StarBtn2, StarBtn3, StarBtn4, StarBtn5 };
            for (int j = 0; j < stars.Length; j++)
            {
                bool filled = j < hasta;
                stars[j].Content = filled ? "★" : "☆";
                stars[j].Foreground = filled
                    ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                    : new SolidColorBrush(Color.FromRgb(136, 142, 158));
            }
        }

        private void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is Theme t && !string.IsNullOrWhiteSpace(t.link_descarga))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(t.link_descarga) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error opening link: " + ex.Message);
                }
            }
        }

        private async void Vote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int val))
                await EnviarVotoAsync(val);
        }

        private async Task EnviarVotoAsync(int valor)
        {
            if (!_session.IsLoggedIn)
            {
                MessageBox.Show("Debes iniciar sesión para votar.", "Login requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!(this.DataContext is Theme theme))
            {
                MessageBox.Show("No hay tema cargado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                bool ok = await _voteRepo.VoteAsync(theme.id, "theme", _session.CurrentUser!.id, valor);
                if (ok)
                {
                    (double avg, int cnt) = await _voteRepo.GetAverageRatingAsync(theme.id, "theme");
                    txtRatingInfo.Text = $"Media: {avg:F2} ({cnt} votos)";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error voting: " + ex.Message);
                MessageBox.Show("No se pudo registrar el voto.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}