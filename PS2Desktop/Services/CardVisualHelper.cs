using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

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
    }
}
