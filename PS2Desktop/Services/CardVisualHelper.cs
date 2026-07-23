using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;

namespace PS2Desktop.Services
{
    public static class CardVisualHelper
    {
        public static void SetupPlaceholderSearch(TextBox txtSearch, string placeholder)
        {
            txtSearch.GotFocus += (s, e) =>
            {
                if (txtSearch.Text == placeholder)
                {
                    txtSearch.Text = "";
                    txtSearch.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextMainBrush");
                }
            };
            txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    txtSearch.Text = placeholder;
                    txtSearch.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextMutedBrush");
                }
            };
        }

        public static CancellationTokenSource? DebounceSearch(TextBox txtSearch, string placeholder, CancellationTokenSource? existingCts, out string searchText, int delayMs = 400)
        {
            existingCts?.Cancel();
            var cts = new CancellationTokenSource();

            if (txtSearch.Text == placeholder || string.IsNullOrWhiteSpace(txtSearch.Text))
                searchText = "";
            else
                searchText = txtSearch.Text.Trim();

            Task.Delay(delayMs, cts.Token).ContinueWith(_ =>
            {
                if (!cts.Token.IsCancellationRequested)
                    txtSearch.Dispatcher.InvokeAsync(() =>
                    {
                        txtSearch.RaiseEvent(new RoutedEventArgs(TextBox.KeyDownEvent, txtSearch));
                    });
            }, cts.Token);

            return cts;
        }

        public static void MostrarSkeleton(WrapPanel skeletonPanel, WrapPanel contentPanel, bool visible)
        {
            if (skeletonPanel == null) return;
            skeletonPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (contentPanel != null)
                contentPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            if (visible && skeletonPanel.Resources["ShimmerAnimation"] is System.Windows.Media.Animation.Storyboard sb)
            {
                foreach (var child in skeletonPanel.Children)
                {
                    if (child is Border skeleton)
                        sb.Begin(skeleton);
                }
            }
        }

        public static void MostrarLoader(Grid loaderOverlay, bool visible)
        {
            if (loaderOverlay == null) return;
            loaderOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public static async void FireAndForget(Func<Task> action, string errorContext)
        {
            try { await action(); }
            catch (Exception ex) { LoggingService.Instance.Error(errorContext, ex); }
        }
        public static bool TryGetCardImage(Border card, out Border? imgBorder, out Image? img)
        {
            imgBorder = null;
            img = null;

            if (card.Child is StackPanel stack && stack.Children.Count > 0
                && stack.Children[0] is Grid imgContainer && imgContainer.Children.Count > 0
                && imgContainer.Children[0] is Border b && b.Child is Image image)
            {
                imgBorder = b;
                img = image;
                return true;
            }
            return false;
        }

        public static bool TrySetCardImage(Border card, BitmapImage? source)
        {
            if (TryGetCardImage(card, out var imgBorder, out var img) && img != null)
            {
                img.Source = source;
                return true;
            }
            return false;
        }

        public static void SetCardImage(Border card, BitmapImage? source, System.Windows.Media.Color backgroundColor)
        {
            if (TryGetCardImage(card, out var imgBorder, out var img))
            {
                if (imgBorder != null)
                {
                    imgBorder.BeginAnimation(Border.OpacityProperty, null);
                    imgBorder.Opacity = 1;
                    imgBorder.Background = new System.Windows.Media.SolidColorBrush(backgroundColor);
                }
                if (img != null)
                {
                    img.Source = source;
                    img.BeginAnimation(Image.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, System.TimeSpan.FromSeconds(0.3)));
                }
            }
        }

        // ── Favoritos: SVG heart helpers ──────────────────────────────────

        private const string HeartOutline = "M16.5 3C14.76 3 13.09 3.81 12 5.09 10.91 3.81 9.24 3 7.5 3 4.42 3 2 5.42 2 8.5c0 3.78 3.4 6.86 8.55 11.54L12 21.35l1.45-1.32C18.6 15.36 22 12.28 22 8.5 22 5.42 19.58 3 16.5 3zm-4.4 15.55l-.1.1-.1-.1C7.14 14.24 4 11.39 4 8.5 4 6.5 5.5 5 7.5 5c1.54 0 3.04.99 3.57 2.36h1.87C13.46 5.99 14.96 5 16.5 5c2 0 3.5 1.5 3.5 3.5 0 2.89-3.14 5.74-7.9 10.05z";
        private const string HeartFilled = "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z";
        private static readonly SolidColorBrush FavActiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x45));
        private static readonly SolidColorBrush FavInactiveBrush = new SolidColorBrush(Colors.White);

        public static void UpdateFavIcon(Path heartPath, bool isFav)
        {
            heartPath.Data = Geometry.Parse(isFav ? HeartFilled : HeartOutline);
            heartPath.Fill = isFav ? FavActiveBrush : Brushes.Transparent;
            heartPath.Stroke = isFav ? FavActiveBrush : FavInactiveBrush;

            var scale = heartPath.RenderTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(1, 1);
                heartPath.RenderTransform = scale;
            }
            var anim = new DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(0.35)) { AutoReverse = true };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        public static Border CreateFavButtonCard(Guid itemId, string itemType, Func<Task> refreshCard)
        {
            var heartPath = new Path
            {
                Data = Geometry.Parse(HeartOutline),
                Stroke = FavInactiveBrush,
                StrokeThickness = 1.8,
                Fill = Brushes.Transparent,
                Stretch = Stretch.Uniform,
                Width = 16,
                Height = 16,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var favBtn = new Border
            {
                Width = 30, Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Agregar a favoritos",
                Child = heartPath
            };

            favBtn.MouseDown += async (s, e) =>
            {
                e.Handled = true;
                var session = App.ServiceProvider.GetRequiredService<Services.Interfaces.ISessionService>();
                if (session.CurrentUser == null) return;
                var favRepo = App.ServiceProvider.GetRequiredService<Services.Interfaces.IFavoriteRepository>();
                await favRepo.ToggleFavoriteAsync(session.CurrentUser.id, itemId, itemType);
                var isFav = await favRepo.IsFavoriteAsync(session.CurrentUser.id, itemId, itemType);
                UpdateFavIcon(heartPath, isFav);
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    var session = App.ServiceProvider.GetRequiredService<Services.Interfaces.ISessionService>();
                    if (session.CurrentUser == null) return;
                    var favRepo = App.ServiceProvider.GetRequiredService<Services.Interfaces.IFavoriteRepository>();
                    var isFav = await favRepo.IsFavoriteAsync(session.CurrentUser.id, itemId, itemType);
                    await heartPath.Dispatcher.InvokeAsync(() => UpdateFavIcon(heartPath, isFav));
                }
                catch (Exception ex) { LoggingService.Instance.Error("Error checking favorite status", ex); }
            });

            return favBtn;
        }

        public static Border CreateFavButtonSidebar(Guid itemId, string itemType, Func<Task>? onToggle = null)
        {
            var heartPath = new Path
            {
                Data = Geometry.Parse(HeartOutline),
                Stroke = FavInactiveBrush,
                StrokeThickness = 1.6,
                Fill = Brushes.Transparent,
                Stretch = Stretch.Uniform,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };

            var label = new TextBlock
            {
                Text = "Agregar a favoritos",
                Foreground = Brushes.White,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(heartPath);
            stack.Children.Add(label);

            var favBtn = new Border
            {
                CornerRadius = new CornerRadius(12),
                Height = 44,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Margin = new Thickness(0, 10, 0, 0),
                ToolTip = "Agregar a favoritos",
                Child = stack
            };

            UpdateFavIcon(heartPath, false);

            CardVisualHelper.FireAndForget(async () =>
            {
                var session = App.ServiceProvider.GetRequiredService<Services.Interfaces.ISessionService>();
                if (session.CurrentUser == null) return;
                var favRepo = App.ServiceProvider.GetRequiredService<Services.Interfaces.IFavoriteRepository>();
                var isFav = await favRepo.IsFavoriteAsync(session.CurrentUser.id, itemId, itemType);
                await heartPath.Dispatcher.InvokeAsync(() =>
                {
                    UpdateFavIcon(heartPath, isFav);
                    label.Text = isFav ? "Quitar de favoritos" : "Agregar a favoritos";
                });
            }, "Error loading favorite status");

            favBtn.MouseDown += async (s, e) =>
            {
                var session = App.ServiceProvider.GetRequiredService<Services.Interfaces.ISessionService>();
                if (session.CurrentUser == null) return;
                var favRepo = App.ServiceProvider.GetRequiredService<Services.Interfaces.IFavoriteRepository>();
                await favRepo.ToggleFavoriteAsync(session.CurrentUser.id, itemId, itemType);
                var isFav = await favRepo.IsFavoriteAsync(session.CurrentUser.id, itemId, itemType);
                UpdateFavIcon(heartPath, isFav);
                label.Text = isFav ? "Quitar de favoritos" : "Agregar a favoritos";
                if (onToggle != null) await onToggle();
            };

            return favBtn;
        }
    }
}
