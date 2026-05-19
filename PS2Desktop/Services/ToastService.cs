using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PS2Desktop.Services
{
    public enum ToastType { Success, Error, Info, Warning }

    public class ToastItem : INotifyPropertyChanged
    {
        private double _opacity = 1;
        public string Message { get; set; } = "";
        public ToastType Type { get; set; }
        public string Icon { get; set; } = "";
        public Brush Background { get; set; } = Brushes.Gray;
        public Brush Foreground { get; set; } = Brushes.White;

        public double Opacity
        {
            get => _opacity;
            set { _opacity = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ToastService
    {
        private static readonly Lazy<ToastService> _instance = new(() => new());
        public static ToastService Instance => _instance.Value;

        private readonly ObservableCollection<ToastItem> _toasts = new();
        private ItemsControl? _container;
        private const double ToastDuration = 3.5;

        public ObservableCollection<ToastItem> Toasts => _toasts;

        public void RegisterContainer(ItemsControl container)
        {
            _container = container;
            _container.ItemsSource = _toasts;
        }

        public void Show(string message, ToastType type = ToastType.Info)
        {
            var item = new ToastItem
            {
                Message = message,
                Type = type,
                Icon = type switch
                {
                    ToastType.Success => "✓",
                    ToastType.Error => "✕",
                    ToastType.Warning => "⚠",
                    _ => "ℹ"
                },
                Background = type switch
                {
                    ToastType.Success => new SolidColorBrush(Color.FromRgb(0x00, 0x6E, 0x3A)),
                    ToastType.Error => new SolidColorBrush(Color.FromRgb(0xC0, 0x28, 0x28)),
                    ToastType.Warning => new SolidColorBrush(Color.FromRgb(0x8A, 0x5E, 0x00)),
                    _ => new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0xCC))
                },
                Foreground = Brushes.White
            };

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _toasts.Insert(0, item);
                if (_toasts.Count > 5)
                    _toasts.RemoveAt(_toasts.Count - 1);
            }));

            Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(ToastDuration));
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_toasts.Contains(item))
                        _toasts.Remove(item);
                }));
            }));
        }

        public void ShowSuccess(string message) => Show(message, ToastType.Success);
        public void ShowError(string message) => Show(message, ToastType.Error);
        public void ShowWarning(string message) => Show(message, ToastType.Warning);
        public void ShowInfo(string message) => Show(message, ToastType.Info);
    }
}
