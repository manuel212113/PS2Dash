using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Vistas
{
    public partial class PerfilView : Window
    {
        private readonly ISessionService _session;
        private readonly IAvatarRepository _avatarRepo;
        private readonly IUserRepository _userRepo;
        private string _selectedAvatarUrl;
        private string _originalAvatarUrl;
        private string _originalDisplayName;
        private bool _nameChanged;
        private bool _avatarChanged;

        public PerfilView()
        {
            InitializeComponent();
            Owner = App.Current.MainWindow;
            _session = App.ServiceProvider.GetRequiredService<ISessionService>();
            _avatarRepo = App.ServiceProvider.GetRequiredService<IAvatarRepository>();
            _userRepo = App.ServiceProvider.GetRequiredService<IUserRepository>();
            Loaded += PerfilView_Loaded;
            TxtDisplayName.TextChanged += TxtDisplayName_TextChanged;
        }

        private async void PerfilView_Loaded(object sender, RoutedEventArgs e)
        {
            var user = _session.CurrentUser;
            if (user == null) return;

            LblUserName.Text = user.display_name ?? user.email;
            _originalAvatarUrl = user.avatar_url;
            _originalDisplayName = user.display_name ?? "";
            TxtDisplayName.Text = _originalDisplayName;
            TxtCharCount.Text = $"{_originalDisplayName.Length}/20";

            if (!string.IsNullOrEmpty(user.avatar_url))
            {
                try { CurrentAvatar.Source = new BitmapImage(new Uri(user.avatar_url)); }
                catch (Exception ex) { LoggingService.Instance.Error("Error loading current avatar", ex); }
            }

            // Entrance: card fade in + scale up
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var scaleUp = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(400))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.15 } };
            WindowCard.BeginAnimation(OpacityProperty, fadeIn);
            WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);

            // Preview glow pulse
            var glowFade = new DoubleAnimation(0.3, 0.8, TimeSpan.FromMilliseconds(1200))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            PreviewGlow.BeginAnimation(OpacityProperty, glowFade);

            await LoadAvatarsAsync();
        }

        private async Task LoadAvatarsAsync()
        {
            try
            {
                var avatars = await _avatarRepo.GetAvatarsAsync();
                var items = new ObservableCollection<AvatarItem>();
                foreach (var (id, name, url) in avatars)
                    items.Add(new AvatarItem { Id = id, Nombre = name, ImageUrl = url });

                AvatarsGrid.ItemsSource = items;

                // Staggered entrance for items
                await Task.Delay(50);
                for (int i = 0; i < items.Count; i++)
                {
                    var container = AvatarsGrid.ItemContainerGenerator.ContainerFromIndex(i)
                        as ContentPresenter;
                    if (container != null)
                    {
                        var border = FindVisualChild<Border>(container);
                        if (border != null)
                        {
                            border.Opacity = 0;
                            var itemFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                            {
                                BeginTime = TimeSpan.FromMilliseconds(i * 40),
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                            };
                            border.BeginAnimation(OpacityProperty, itemFade);

                            // Hover effect
                            var origScale = 1.0;
                            border.MouseEnter += (s, e) =>
                            {
                                var up = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(150))
                                {
                                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                                };
                                border.RenderTransform = new ScaleTransform(1, 1);
                                border.RenderTransformOrigin = new Point(0.5, 0.5);
                                border.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, up);
                                border.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, up);
                            };
                            border.MouseLeave += (s, e) =>
                            {
                                var down = new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(150))
                                {
                                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                                };
                                if (border.RenderTransform is ScaleTransform st)
                                {
                                    st.BeginAnimation(ScaleTransform.ScaleXProperty, down);
                                    st.BeginAnimation(ScaleTransform.ScaleYProperty, down);
                                }
                            };
                        }
                    }
                }
            }
            catch (Exception ex) { LoggingService.Instance.Error("Error loading avatars", ex); }
        }

        private void TxtDisplayName_TextChanged(object sender, TextChangedEventArgs e)
        {
            var len = TxtDisplayName.Text.Length;
            TxtCharCount.Text = $"{len}/20";
            TxtCharCount.Foreground = len >= 20
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35))
                : new SolidColorBrush(Color.FromRgb(0x5A, 0x6A, 0x78));
            _nameChanged = TxtDisplayName.Text != _originalDisplayName;
            UpdateSaveButtonState();
        }

        private void UpdateSaveButtonState()
        {
            BtnSave.IsEnabled = _nameChanged || _avatarChanged;
            if (BtnSave.IsEnabled)
            {
                var glowAnim = new ColorAnimation(
                    Color.FromRgb(0, 0x55, 0xCC),
                    Color.FromRgb(0, 0x77, 0xEE),
                    TimeSpan.FromMilliseconds(1200))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                BtnSave.Background = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC));
                BtnSave.Background.BeginAnimation(SolidColorBrush.ColorProperty, glowAnim);
            }
            else
            {
                BtnSave.Background = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC));
                BtnSave.Background.BeginAnimation(SolidColorBrush.ColorProperty, null);
            }
        }

        private void Avatar_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string url && !string.IsNullOrEmpty(url))
            {
                _selectedAvatarUrl = url;
                _avatarChanged = url != _originalAvatarUrl;
                try { CurrentAvatar.Source = new BitmapImage(new Uri(url)); } catch (Exception ex) { LoggingService.Instance.Error("Error loading avatar preview", ex); }

                // Cross-fade preview
                var fadeOut = new DoubleAnimation(1, 0.5, TimeSpan.FromMilliseconds(150))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                var fadeIn = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(250))
                    { BeginTime = TimeSpan.FromMilliseconds(150),
                      EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                CurrentAvatar.BeginAnimation(OpacityProperty, fadeOut);
                CurrentAvatar.BeginAnimation(OpacityProperty, fadeIn);

                // Highlight selected avatar
                RemoveSelectionHighlights();
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 0x55, 0xCC));
                var highlightAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                border.BeginAnimation(OpacityProperty, null);
                border.Opacity = 1;

                UpdateSaveButtonState();
            }
        }

        private void RemoveSelectionHighlights()
        {
            foreach (var item in AvatarsGrid.Items)
            {
                var container = AvatarsGrid.ItemContainerGenerator.ContainerFromItem(item);
                if (container == null) continue;

                // Find the Border in the visual tree
                var border = FindVisualChild<Border>(container as ContentPresenter);
                if (border != null)
                    border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            BtnSave.IsEnabled = false;

            var nuevoNombre = TxtDisplayName.Text?.Trim();
            if (string.IsNullOrEmpty(nuevoNombre))
            {
                ToastService.Instance.ShowWarning("El nombre no puede estar vacío.");
                BtnSave.IsEnabled = true;
                return;
            }

            try
            {
                var user = _session.CurrentUser;

                if (_avatarChanged && !string.IsNullOrEmpty(_selectedAvatarUrl))
                {
                    await _userRepo.UpdateUserAvatarAsync(user.id, _selectedAvatarUrl);
                    user.avatar_url = _selectedAvatarUrl;
                }

                if (_nameChanged)
                {
                    var actualizado = await _userRepo.UpdateUserAsync(user.id, nuevoNombre);
                    if (actualizado != null)
                        _session.CurrentUser = actualizado;
                }

                DialogResult = true;

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, a) => Close();
                WindowCard.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError("Error: " + ex.Message);
                LoggingService.Instance.Error("Error al guardar perfil", ex);
                BtnSave.IsEnabled = true;
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            CerrarConAnimacion();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CerrarConAnimacion();
            }
        }

        private void CerrarConAnimacion()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, a) => Close();
            WindowCard.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    public class AvatarItem
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; }
        public string ImageUrl { get; set; }
    }
}
