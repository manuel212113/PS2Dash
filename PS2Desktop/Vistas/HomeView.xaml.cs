using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PS2Desktop.Vistas
{
    public partial class HomeView : UserControl
    {
        private List<BitmapFrame> _gifFrames;
        private int _gifFrameIndex;
        private DispatcherTimer _gifTimer;

        private readonly IThemeRepository _themeRepo;
        private readonly IGameRepository _gameRepo;
        private readonly IUserRepository _userRepo;
        private readonly IVoteRepository _voteRepo;

        public event EventHandler NavigateToTemas;
        public event EventHandler NavigateToJuegos;

        public HomeView()
        {
            InitializeComponent();
            _themeRepo = App.ServiceProvider.GetRequiredService<IThemeRepository>();
            _gameRepo = App.ServiceProvider.GetRequiredService<IGameRepository>();
            _userRepo = App.ServiceProvider.GetRequiredService<IUserRepository>();
            _voteRepo = App.ServiceProvider.GetRequiredService<IVoteRepository>();
            this.Loaded += HomeView_Loaded;
        }

        private async void HomeView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPS2Model();
            StartContinuousAnimations();
            AnimateStaggeredEntrance();
            WireHoverEffects();
            WireNavigation();
            await LoadStatsAsync();
        }

        private void LoadPS2Model()
        {
            try
            {
                var uri = new Uri("pack://siteoforigin:,,,/Imagenes/ps2modelo.gif", UriKind.Absolute);
                var decoder = new GifBitmapDecoder(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                _gifFrames = new List<BitmapFrame>(decoder.Frames);
                if (_gifFrames.Count > 0)
                {
                    PS2ModelImage.Source = _gifFrames[0];
                    _gifFrameIndex = 0;
                    _gifTimer = new DispatcherTimer();
                    _gifTimer.Interval = TimeSpan.FromMilliseconds(50);
                    _gifTimer.Tick += (s, e) =>
                    {
                        _gifFrameIndex = (_gifFrameIndex + 1) % _gifFrames.Count;
                        PS2ModelImage.Source = _gifFrames[_gifFrameIndex];
                    };
                    _gifTimer.Start();
                }
            }
            catch { }
        }

        private void StartContinuousAnimations()
        {
            if (FindResource("GlowPulse") is Storyboard glow) glow.Begin(this);
            if (FindResource("FloatAnim") is Storyboard floatAnim) floatAnim.Begin(this);
            if (FindResource("SweepAnim") is Storyboard sweep) sweep.Begin(this);
        }

        private void AnimateStaggeredEntrance()
        {
            var cards = new (FrameworkElement element, double delay)[]
            {
                (StatCard1, 0.0), (StatCard2, 0.1), (StatCard3, 0.2), (StatCard4, 0.3),
                (DemoCard1, 0.4), (DemoCard2, 0.5), (DemoCard3, 0.6),
                (QACard1, 0.7), (QACard2, 0.8), (QACard3, 0.9),
            };
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            foreach (var (element, delay) in cards)
            {
                element.Opacity = 0;
                element.RenderTransformOrigin = new Point(0.5, 0.5);
                var translate = new TranslateTransform(0, 40);
                var group = new TransformGroup();
                if (element.RenderTransform is Transform existing && existing != Transform.Identity)
                    group.Children.Add(existing);
                else
                    group.Children.Add(new ScaleTransform(1, 1));
                group.Children.Add(translate);
                element.RenderTransform = group;

                element.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(delay) });
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(40, 0, TimeSpan.FromSeconds(0.6)) { BeginTime = TimeSpan.FromSeconds(delay), EasingFunction = ease });
            }
        }

        private void WireHoverEffects()
        {
            WireScaleHover(DemoCard1, CardScale1, 1.05);
            WireScaleHover(DemoCard2, CardScale2, 1.05);
            WireScaleHover(DemoCard3, CardScale3, 1.05);
            WireScaleHover(QACard1, QAScale1, 1.04);
            WireScaleHover(QACard2, QAScale2, 1.04);
            WireScaleHover(QACard3, QAScale3, 1.04);
        }

        private void WireScaleHover(FrameworkElement element, ScaleTransform scale, double scaleTo)
        {
            var duration = TimeSpan.FromSeconds(0.2);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            element.MouseEnter += (s, e) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, scaleTo, duration) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, scaleTo, duration) { EasingFunction = ease });
            };
            element.MouseLeave += (s, e) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleTo, 1, duration) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleTo, 1, duration) { EasingFunction = ease });
            };
        }

        private async Task LoadStatsAsync()
        {
            try
            {
                var taskTheme = _themeRepo.GetThemeCountAsync();
                var taskGame = _gameRepo.GetGameCountAsync();
                var taskUser = _userRepo.GetUserCountAsync();
                var taskRating = _voteRepo.GetGlobalAverageRatingAsync();

                await Task.WhenAll(taskTheme, taskGame, taskUser, taskRating);

                StatTemas.Text = (await taskTheme).ToString();
                StatDemos.Text = (await taskGame).ToString();
                StatUsers.Text = (await taskUser).ToString();
                var (avg, _) = await taskRating;
                StatRating.Text = avg > 0 ? avg.ToString("F1") : "0.0";
            }
            catch { }
        }

        private void WireNavigation()
        {
            BtnExplorarDemos.Click += (s, e) => NavigateToJuegos?.Invoke(this, EventArgs.Empty);
            BtnVerTemas.Click += (s, e) => NavigateToTemas?.Invoke(this, EventArgs.Empty);
            QACard1.MouseDown += (s, e) => NavigateToTemas?.Invoke(this, EventArgs.Empty);
            QACard2.MouseDown += (s, e) => NavigateToJuegos?.Invoke(this, EventArgs.Empty);
        }
    }
}
