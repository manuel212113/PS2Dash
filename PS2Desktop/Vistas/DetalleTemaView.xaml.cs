using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PS2Desktop.Modelos;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Vistas
{
    public partial class DetalleTemaView : UserControl
    {
        public event EventHandler Volver;

        private readonly IVoteRepository _voteRepo;
        private readonly ISessionService _session;
        private Theme _temaActual;
        private int _userVote = 0;

        public DetalleTemaView()
        {
            InitializeComponent();

            _voteRepo = App.ServiceProvider.GetRequiredService<IVoteRepository>();
            _session = App.ServiceProvider.GetRequiredService<ISessionService>();

            this.Loaded += (s, e) => MainScrollViewer.Focus();
        }

        public void SetTema(Theme tema)
        {
            _temaActual = tema;
            this.DataContext = tema;

            lblTitle.Text = tema.nombre ?? "Sin título";
            lblDescripcion.Text = tema.descripcion ?? "Sin descripción";

            if (tema.caracteristicas != null && tema.caracteristicas.Count > 0)
            {
                CaracteristicasList.ItemsSource = tema.caracteristicas;
                CaracteristicasList.Visibility = Visibility.Visible;
            }
            else
            {
                CaracteristicasList.Visibility = Visibility.Collapsed;
            }

            CardVisualHelper.FireAndForget(() => CargarRatingAsync(tema.id), "Error cargando rating");

            lblPrice.Text = "GRATIS";
            btnDownload.Content = string.IsNullOrEmpty(tema.link_descarga) ? "No disponible" : "CONSEGUIR";
            btnDownload.IsEnabled = !string.IsNullOrEmpty(tema.link_descarga);

            if (_session.IsLoggedIn)
            {
                var favBtn = CardVisualHelper.CreateFavButtonSidebar(tema.id, "theme");
                var sidebar = (btnDownload.Parent as StackPanel);
                if (sidebar != null)
                {
                    var idx = sidebar.Children.IndexOf(btnDownload) + 1;
                    sidebar.Children.Insert(idx, favBtn);
                }
            }

            if (!string.IsNullOrEmpty(tema.image_url))
            {
                try
                {
                    var img = new BitmapImage(new Uri(tema.image_url, UriKind.Absolute));
                    ThemeImage.Source = img;
                    lblNoImage.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ThemeImage.Source = null;
                    lblNoImage.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ThemeImage.Source = null;
                lblNoImage.Visibility = Visibility.Visible;
            }

            VotePanel.Visibility = Visibility.Visible;
            ConfigurarEstrellas();
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
            catch (Exception ex) { LoggingService.Instance.Error("Error loading ratings", ex); }
        }

        private void btnVolver_Click(object sender, RoutedEventArgs e) => Volver?.Invoke(this, EventArgs.Empty);

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

        public async Task ApplyTheme(Theme theme)
        {
            if (theme == null) return;
            SetTema(theme);
            await Task.CompletedTask;
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
                    CardVisualHelper.FireAndForget(() => EnviarVotoAsync(idx), "Error enviando voto");
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
                    if (!Uri.TryCreate(t.link_descarga, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                    {
                        Debug.WriteLine("Invalid download link: " + t.link_descarga);
                        return;
                    }
                    Process.Start(new ProcessStartInfo(t.link_descarga) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error opening link: " + ex.Message);
                }
            }
        }

        private async Task EnviarVotoAsync(int valor)
        {
            if (!_session.IsLoggedIn)
            {
                ToastService.Instance.ShowWarning("Debes iniciar sesión para votar.");
                return;
            }

            if (!(this.DataContext is Theme theme))
            {
                ToastService.Instance.ShowError("No hay tema cargado.");
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
                ToastService.Instance.ShowError("No se pudo registrar el voto.");
            }
        }
    }
}
