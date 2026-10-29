using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EveryGo
{
    internal sealed class MainWindow : Window
    {
        private readonly ConfigStore _store;
        private readonly Config _config;
        private readonly string _loadNotice;

        private Border _card;
        private Grid _gridHost;
        private ToolbarIcon _pinIcon;
        private SettingsWindow _settings;

        private DispatcherTimer _saveTimer;
        private DispatcherTimer _toastTimer;
        private Border _toast;
        private TextBlock _toastText;

        private bool _readyForSave;

        public MainWindow(ConfigStore store, Config config, string loadNotice)
        {
            _store = store;
            _config = config;
            _loadNotice = loadNotice;
            _config.Normalize();

            InitializeWindow();
            BuildUi();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += OnSaveTimerTick;

            ApplyConfig();
            ApplyWindowSize();
            RenderGrid();
            RestorePosition();

            Loaded += OnLoaded;
            LocationChanged += OnLocationChanged;
            Closing += OnClosing;
            Closed += OnClosed;
        }

        public Config Config { get { return _config; } }
        public ConfigStore Store { get { return _store; } }
        public string DataDirectory { get { return _store.Paths.DataDir; } }

        private void InitializeWindow()
        {
            Title = "EveryGo";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            Background = Brushes.Transparent;
            FontFamily = Theme.Font;
            UseLayoutRounding = true;
            SnapsToDevicePixels = false;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        }

        private void BuildUi()
        {
            Grid root = new Grid { Background = Brushes.Transparent };
            root.Children.Add(BuildCard());
            BuildToast(root);
            Content = root;
        }

        private FrameworkElement BuildCard()
        {
            _card = new Border
            {
                Margin = new Thickness(LayoutMetrics.WindowShadowMargin),
                CornerRadius = new CornerRadius(Theme.CardRadius),
                BorderThickness = new Thickness(LayoutMetrics.CardBorderWidth),
                BorderBrush = Theme.CardRimBrush,
                Background = Theme.CardBackground(_config.appearance.color, _config.appearance.opacity),
                Effect = Theme.CardShadow()
            };

            Grid cardContent = new Grid();

            Grid layout = new Grid
            {
                Margin = new Thickness(
                    LayoutMetrics.CardPaddingX,
                    LayoutMetrics.ContentTopPadding,
                    LayoutMetrics.CardPaddingX,
                    LayoutMetrics.ContentBottomPadding)
            };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            layout.Children.Add(BuildToolbar());

            _gridHost = new Grid
            {
                Margin = new Thickness(0, LayoutMetrics.ToolbarToGridGap, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(_gridHost, 1);
            layout.Children.Add(_gridHost);

            cardContent.Children.Add(layout);
            _card.Child = cardContent;
            _card.MouseLeftButtonDown += OnCardMouseDown;
            return _card;
        }

        private FrameworkElement BuildToolbar()
        {
            Grid bar = new Grid
            {
                Height = LayoutMetrics.ToolbarHeight,
                VerticalAlignment = VerticalAlignment.Top
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement handle = BuildDragHandle();
            handle.HorizontalAlignment = HorizontalAlignment.Left;
            handle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(handle, 0);
            bar.Children.Add(handle);

            StackPanel icons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(icons, 2);

            _pinIcon = new ToolbarIcon("窗口置顶", IconKind.Pin);
            _pinIcon.MouseLeftButtonUp += delegate { ToggleTopmost(); };

            ToolbarIcon settingsIcon = new ToolbarIcon("设置", IconKind.Gear);
            settingsIcon.MouseLeftButtonUp += delegate { OpenSettings(); };

            ToolbarIcon minimizeIcon = new ToolbarIcon("最小化", IconKind.Minimize);
            minimizeIcon.MouseLeftButtonUp += delegate { WindowState = WindowState.Minimized; };

            ToolbarIcon closeIcon = new ToolbarIcon("关闭", IconKind.Close);
            closeIcon.MouseLeftButtonUp += delegate { RequestExit(); };

            ToolbarIcon[] all = new ToolbarIcon[] { _pinIcon, settingsIcon, minimizeIcon, closeIcon };
            for (int i = 0; i < all.Length; i++)
            {
                all[i].Margin = new Thickness(0, 0, i < all.Length - 1 ? 4 : 0, 0);
                icons.Children.Add(all[i]);
            }

            bar.Children.Add(icons);
            return bar;
        }

        private FrameworkElement BuildDragHandle()
        {
            Canvas canvas = new Canvas { Width = 18, Height = 18, Cursor = Cursors.SizeAll };
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 2; column++)
                {
                    Ellipse dot = new Ellipse
                    {
                        Width = 2.6,
                        Height = 2.6,
                        Fill = Theme.HandleDotBrush
                    };
                    Canvas.SetLeft(dot, 5 + column * 6);
                    Canvas.SetTop(dot, 4 + row * 5.5);
                    canvas.Children.Add(dot);
                }
            }
            Border wrapper = new Border
            {
                Child = canvas,
                Background = Brushes.Transparent,
                ToolTip = UiControls.MakeToolTip("拖动面板")
            };
            ToolTipService.SetInitialShowDelay(wrapper, 350);
            return wrapper;
        }

        private void ApplyConfig()
        {
            Topmost = _config.window.topmost;
            if (_pinIcon != null) _pinIcon.SetActive(_config.window.topmost);
        }

        private void ApplyWindowSize()
        {
            int columns = _config.gridColumns;
            Width = LayoutMetrics.WindowWidth(columns);
            _gridHost.Height = LayoutMetrics.GridHeight(1, LayoutMetrics.TileSize(columns));

            double maxHeight = SystemParameters.WorkArea.Height * 0.8;
            double desiredHeight = LayoutMetrics.WindowHeight(1, columns);
            MaxHeight = maxHeight;
            Height = Math.Min(desiredHeight, maxHeight);
            ClampToWorkArea();
        }

        private void RenderGrid()
        {
            if (_gridHost == null) return;
            _gridHost.Children.Clear();

            if (_config.apps.Count == 0)
            {
                double tileSize = LayoutMetrics.TileSize(_config.gridColumns);
                FrameworkElement addTile = CreateAddTile(tileSize);
                addTile.HorizontalAlignment = HorizontalAlignment.Center;
                addTile.VerticalAlignment = VerticalAlignment.Center;
                _gridHost.Children.Add(addTile);
            }
            // Phase 2：非空状态在此渲染应用图标与「＋」。
        }

        private FrameworkElement CreateAddTile(double size)
        {
            Grid visual = new Grid { Width = size, Height = size };

            Rectangle dash = new Rectangle
            {
                RadiusX = LayoutMetrics.TileRadius,
                RadiusY = LayoutMetrics.TileRadius,
                Stroke = Theme.PlusBorderBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            visual.Children.Add(dash);

            FrameworkElement plus = IconFactory.Create(IconKind.Plus, 24);
            plus.Opacity = 0.85;
            plus.HorizontalAlignment = HorizontalAlignment.Center;
            plus.VerticalAlignment = VerticalAlignment.Center;
            visual.Children.Add(plus);

            Border hit = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(LayoutMetrics.TileRadius),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = visual,
                ToolTip = UiControls.MakeToolTip("添加应用"),
                Tag = "interactive"
            };
            ToolTipService.SetInitialShowDelay(hit, 350);

            hit.MouseEnter += delegate
            {
                hit.Background = Theme.PlusHoverBrush;
                plus.Effect = new DropShadowEffect
                {
                    Color = Colors.White,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.16,
                    RenderingBias = RenderingBias.Performance
                };
                AnimateOpacity(plus, 1.0);
            };
            hit.MouseLeave += delegate
            {
                hit.Background = Brushes.Transparent;
                plus.Effect = null;
                AnimateOpacity(plus, 0.85);
            };
            hit.MouseLeftButtonUp += delegate
            {
                ShowToast("Phase 1 骨架：添加应用将在 Phase 2 接入。", false);
            };
            return hit;
        }

        private void ToggleTopmost()
        {
            ApplyTopmost(!_config.window.topmost);
        }

        public void ApplyTopmost(bool topmost)
        {
            _config.window.topmost = topmost;
            Topmost = topmost;
            if (_pinIcon != null) _pinIcon.SetActive(topmost);
            ScheduleSave();
        }

        public void SetGridColumns(int columns)
        {
            if (!Config.IsValidGridColumnCount(columns) || columns == _config.gridColumns) return;
            _config.gridColumns = columns;
            ApplyWindowSize();
            RenderGrid();
            ScheduleSave();
        }

        public void ScheduleSave()
        {
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void SaveNow()
        {
            if (_saveTimer != null) _saveTimer.Stop();
            try
            {
                _store.Save(_config);
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败", ex);
                ShowToast("配置保存失败：" + ex.Message, true);
            }
        }

        private void OnSaveTimerTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNow();
        }

        private void OpenSettings()
        {
            if (_settings != null)
            {
                if (_settings.WindowState == WindowState.Minimized)
                {
                    _settings.WindowState = WindowState.Normal;
                }
                _settings.Activate();
                return;
            }

            _settings = new SettingsWindow(this);
            _settings.Closed += delegate { _settings = null; };
            _settings.Show();
        }

        private void RequestExit()
        {
            SaveNow();
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
            }
        }

        private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (UiControls.IsInteractive(e.OriginalSource)) return;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 窗口状态切换时拖动可能失败，忽略即可。
            }
        }

        private void OnLocationChanged(object sender, EventArgs e)
        {
            if (!_readyForSave) return;
            if (WindowState != WindowState.Normal) return;
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            _config.window.x = (int)Math.Round(Left);
            _config.window.y = (int)Math.Round(Top);
            ScheduleSave();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _readyForSave = true;
            if (!string.IsNullOrEmpty(_loadNotice))
            {
                ShowToast(_loadNotice, true);
            }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveNow();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (_saveTimer != null) _saveTimer.Stop();
            if (_toastTimer != null) _toastTimer.Stop();
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
            }
        }

        private void RestorePosition()
        {
            if (double.IsNaN(Width) || double.IsNaN(Height)) return;

            bool hasPosition = _config.window.x.HasValue && _config.window.y.HasValue;
            if (hasPosition)
            {
                Left = _config.window.x.Value;
                Top = _config.window.y.Value;

                Rect windowRect = new Rect(Left, Top, Width, Height);
                Rect virtualRect = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
                Rect intersection = Rect.Intersect(windowRect, virtualRect);
                double visibleArea = intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
                double windowArea = windowRect.Width * windowRect.Height;

                if (visibleArea < windowArea * 0.35)
                {
                    CenterInWorkArea();
                }
                else
                {
                    ClampToWorkArea();
                }
                return;
            }

            CenterInWorkArea();
        }

        private void CenterInWorkArea()
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }

        private void ClampToWorkArea()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            Rect area = SystemParameters.WorkArea;

            if (Left + Width > area.Right) Left = area.Right - Width;
            if (Top + Height > area.Bottom) Top = area.Bottom - Height;
            if (Left < area.Left) Left = area.Left;
            if (Top < area.Top) Top = area.Top;
        }

        private void BuildToast(Grid root)
        {
            _toastText = new TextBlock
            {
                Foreground = Theme.TextPrimaryBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _toast = new Border
            {
                Background = Theme.ToastBackgroundBrush,
                BorderBrush = Theme.CardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7, 12, 7),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(26, 0, 26, 26),
                MaxWidth = 300,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Opacity = 0,
                Child = _toastText
            };
            root.Children.Add(_toast);

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                FadeOutToast();
            };
        }

        private void ShowToast(string message, bool warning)
        {
            if (_toast == null) return;
            _toastText.Text = message;
            _toastText.Foreground = warning ? Theme.AccentBrush : Theme.TextPrimaryBrush;
            _toast.Visibility = Visibility.Visible;

            _toast.BeginAnimation(UIElement.OpacityProperty, null);
            _toast.Opacity = 0;
            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160));
            fadeIn.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            fadeIn.FillBehavior = FillBehavior.HoldEnd;
            _toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (_toastTimer != null)
            {
                _toastTimer.Stop();
                _toastTimer.Start();
            }
        }

        private void FadeOutToast()
        {
            if (_toast == null) return;
            DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fadeOut.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            fadeOut.FillBehavior = FillBehavior.HoldEnd;
            fadeOut.Completed += delegate
            {
                _toast.Visibility = Visibility.Collapsed;
            };
            _toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private static void AnimateOpacity(UIElement element, double target)
        {
            DoubleAnimation animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(150));
            animation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            animation.FillBehavior = FillBehavior.HoldEnd;
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }
}
