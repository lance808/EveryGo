using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EveryGo
{
    /// <summary>应用图标方块：72/64/56 方块 + 名称、悬停上浮、按下缩放、缺失文件提示。</summary>
    internal sealed class AppTile : Grid
    {
        private readonly Border _surface;
        private readonly Image _icon;
        private readonly Ellipse _warningDot;
        private readonly TextBlock _name;
        private readonly ScaleTransform _pressScale;
        private readonly TranslateTransform _hoverLift;
        private readonly ScaleTransform _appearScale;

        private bool _pressed;
        private long _lastLaunchTicks;

        public AppEntry Entry { get; private set; }
        public string IconKey { get; set; }
        public bool IsMissing { get; private set; }

        public event EventHandler LaunchRequested;

        public AppTile(AppEntry entry, int columns)
        {
            Entry = entry;
            double tileSize = LayoutMetrics.TileSize(columns);
            double iconSize = LayoutMetrics.IconSizeFor(columns);
            double itemHeight = LayoutMetrics.ItemHeight(columns, true);

            Width = tileSize;
            Height = itemHeight;
            Background = Brushes.Transparent;
            Tag = "interactive";
            RenderTransformOrigin = new Point(0.5, 0.5);
            _appearScale = new ScaleTransform(1, 1);
            RenderTransform = _appearScale;

            _pressScale = new ScaleTransform(1, 1);
            _hoverLift = new TranslateTransform(0, 0);
            TransformGroup surfaceTransform = new TransformGroup();
            surfaceTransform.Children.Add(_pressScale);
            surfaceTransform.Children.Add(_hoverLift);

            _surface = new Border
            {
                Width = tileSize,
                Height = tileSize,
                CornerRadius = new CornerRadius(LayoutMetrics.TileRadius),
                Background = Theme.TileBackgroundBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = surfaceTransform
            };

            _icon = new Image
            {
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Source = IconCache.GetDefaultIcon()
            };
            RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);

            _warningDot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 6),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            Grid surfaceContent = new Grid();
            surfaceContent.Children.Add(_icon);
            surfaceContent.Children.Add(_warningDot);
            _surface.Child = surfaceContent;

            _name = new TextBlock
            {
                Text = entry == null ? "" : entry.name,
                Width = tileSize,
                Height = LayoutMetrics.LabelHeight,
                Margin = new Thickness(0, LayoutMetrics.LabelGap, 0, 0),
                FontSize = 11,
                Foreground = Theme.TextPrimaryBrush,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            _name.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 1.5,
                ShadowDepth = 1,
                Direction = 90,
                Opacity = 0.45,
                RenderingBias = RenderingBias.Performance
            };

            StackPanel stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };
            stack.Children.Add(_surface);
            stack.Children.Add(_name);
            Children.Add(stack);

            ToolTipService.SetInitialShowDelay(this, 350);
            ToolTipService.SetShowDuration(this, 6000);

            MouseEnter += delegate
            {
                _surface.Background = Theme.TileHoverBrush;
                AnimateLift(-2.0);
            };
            MouseLeave += delegate
            {
                _surface.Background = Theme.TileBackgroundBrush;
                AnimateLift(0);
                if (_pressed)
                {
                    _pressed = false;
                    AnimatePress(1.0, 150);
                }
            };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState != MouseButtonState.Pressed) return;
                _pressed = true;
                AnimatePress(0.96, 100);
                e.Handled = true;
            };
            MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!_pressed) return;
                _pressed = false;
                AnimatePress(1.0, 150);
                if (AcceptLaunch() && LaunchRequested != null)
                {
                    LaunchRequested(this, EventArgs.Empty);
                }
                e.Handled = true;
            };

            RefreshMissing();
        }

        public void SetIcon(BitmapSource source)
        {
            if (source != null) _icon.Source = source;
        }

        public void RefreshMissing()
        {
            bool missing = Entry == null
                || string.IsNullOrEmpty(Entry.path)
                || !System.IO.File.Exists(Entry.path);
            SetMissing(missing);
        }

        public void SetMissing(bool missing)
        {
            IsMissing = missing;
            _icon.Opacity = missing ? 0.6 : 1.0;
            _warningDot.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
            ToolTip = UiControls.MakeToolTip(missing ? "文件不存在" : (Entry == null ? "" : Entry.path));
        }

        public bool AcceptLaunch()
        {
            long now = DateTime.UtcNow.Ticks;
            long interval = TimeSpan.TicksPerMillisecond * 400;
            if (_lastLaunchTicks != 0 && now - _lastLaunchTicks < interval) return false;
            _lastLaunchTicks = now;
            return true;
        }

        public void PlayAppear()
        {
            Opacity = 1.0;
            DoubleAnimation fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
            fade.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            fade.FillBehavior = FillBehavior.Stop;
            BeginAnimation(OpacityProperty, fade);

            AnimateAppearScale(0.86, 1.0, 180, false, null);
        }

        public void PlayDisappear(Action completed)
        {
            DoubleAnimation fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140));
            fade.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            fade.FillBehavior = FillBehavior.HoldEnd;
            BeginAnimation(OpacityProperty, fade);
            AnimateAppearScale(1.0, 0.94, 140, true, completed);
        }

        public void PlayAttention()
        {
            AnimateAppearScale(1.0, 1.06, 100, true, null, true);
        }

        private void AnimateAppearScale(double from, double to, int milliseconds, bool hold, Action completed)
        {
            AnimateAppearScale(from, to, milliseconds, hold, completed, false);
        }

        private void AnimateAppearScale(double from, double to, int milliseconds, bool hold, Action completed, bool autoReverse)
        {
            DoubleAnimation scaleX = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds));
            scaleX.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scaleX.FillBehavior = hold ? FillBehavior.HoldEnd : FillBehavior.Stop;
            if (autoReverse)
            {
                scaleX.AutoReverse = true;
                scaleX.FillBehavior = FillBehavior.Stop;
            }

            DoubleAnimation scaleY = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds));
            scaleY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scaleY.FillBehavior = hold ? FillBehavior.HoldEnd : FillBehavior.Stop;
            if (autoReverse)
            {
                scaleY.AutoReverse = true;
                scaleY.FillBehavior = FillBehavior.Stop;
            }

            if (completed != null)
            {
                scaleX.Completed += delegate { completed(); };
            }
            _appearScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            _appearScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private void AnimatePress(double target, int milliseconds)
        {
            DoubleAnimation scaleX = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds));
            scaleX.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scaleX.FillBehavior = FillBehavior.HoldEnd;
            DoubleAnimation scaleY = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds));
            scaleY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scaleY.FillBehavior = FillBehavior.HoldEnd;
            _pressScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            _pressScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private void AnimateLift(double target)
        {
            DoubleAnimation animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(150));
            animation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            animation.FillBehavior = FillBehavior.HoldEnd;
            _hoverLift.BeginAnimation(TranslateTransform.YProperty, animation);
        }
    }
}
