using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EveryGo
{
    internal sealed class MainWindow : Window
    {
        private readonly ConfigStore _store;
        private readonly Config _config;
        private string _startupNotice;

        private Border _card;
        private Grid _cardContent;
        private Border _baseLayer;
        private Image _backgroundImage;
        private Border _tintLayer;
        private Border _bottomShade;
        private Grid _gridHost;
        private ScrollViewer _gridScroll;
        private ToolbarIcon _pinIcon;
        private SettingsWindow _settings;
        private TrayService _tray;
        private string _loadedBackgroundPath;
        private bool _backgroundNoticeShown;
        private bool _glassFallbackLogged;

        private DispatcherTimer _saveTimer;
        private DispatcherTimer _toastTimer;
        private DispatcherTimer _iconRefreshTimer;
        private Border _toast;
        private TextBlock _toastText;

        private readonly Dictionary<string, AppTile> _tiles =
            new Dictionary<string, AppTile>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _iconFallbackNotified =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string _pendingAppearId;
        private bool _readyForSave;
        private bool _exiting;

        public MainWindow(ConfigStore store, Config config, string loadNotice)
        {
            _store = store;
            _config = config;
            _startupNotice = loadNotice;
            _config.Normalize();

            InitializeWindow();
            BuildUi();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += OnSaveTimerTick;

            _iconRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _iconRefreshTimer.Tick += delegate
            {
                if (IsVisible) RefreshTiles();
            };
            _iconRefreshTimer.Start();

            ApplyConfig();
            ApplyAppearance();
            RebuildPanel();
            RestorePosition();
            ApplyTrayAtStartup();

            Loaded += OnLoaded;
            LocationChanged += OnLocationChanged;
            Activated += delegate { RefreshTiles(); };
            Closing += OnClosing;
            Closed += OnClosed;
        }

        public Config Config { get { return _config; } }
        public ConfigStore Store { get { return _store; } }
        public string DataDirectory { get { return _store.Paths.DataDir; } }
        public string ExePath { get { return _store.Paths.ExePath; } }

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
                Background = Brushes.Transparent,
                Effect = Theme.CardShadow()
            };

            _cardContent = new Grid { Background = Brushes.Transparent };
            _cardContent.SizeChanged += delegate { UpdateCardClip(); };

            _baseLayer = new Border { IsHitTestVisible = false };
            _cardContent.Children.Add(_baseLayer);

            _backgroundImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(_backgroundImage, BitmapScalingMode.HighQuality);
            _cardContent.Children.Add(_backgroundImage);

            _tintLayer = new Border { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _cardContent.Children.Add(_tintLayer);

            _bottomShade = new Border
            {
                Visibility = Visibility.Collapsed,
                Background = Theme.BottomShadeBrush,
                IsHitTestVisible = false
            };
            _cardContent.Children.Add(_bottomShade);

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

            _gridHost = new Grid();
            _gridScroll = new ScrollViewer
            {
                Content = _gridHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Focusable = false,
                IsTabStop = false,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, LayoutMetrics.ToolbarToGridGap, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(_gridScroll, 1);
            layout.Children.Add(_gridScroll);

            _cardContent.Children.Add(layout);
            _card.Child = _cardContent;
            _card.MouseLeftButtonDown += OnCardMouseDown;
            _card.ContextMenu = BuildPanelMenu();
            return _card;
        }

        private void UpdateCardClip()
        {
            if (_cardContent == null) return;
            double width = _cardContent.ActualWidth;
            double height = _cardContent.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                _cardContent.Clip = null;
                return;
            }
            _cardContent.Clip = new RectangleGeometry(
                new Rect(0, 0, width, height),
                Theme.CardRadius,
                Theme.CardRadius);
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
            closeIcon.MouseLeftButtonUp += delegate { RequestClose(); };

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

        /// <summary>外观实时预览：改配置 → 立刻重绘 → 400ms 防抖保存。</summary>
        public void UpdateAppearance(Action<AppearanceConfig> mutate)
        {
            if (mutate == null) return;
            mutate(_config.appearance);
            _config.appearance.Normalize();
            ApplyAppearance();
            ScheduleSave();
        }

        public void ResetAppearance()
        {
            _config.appearance = new AppearanceConfig();
            _loadedBackgroundPath = null;
            _backgroundNoticeShown = false;
            ApplyAppearance();
            ScheduleSave();
            Logger.Info("外观已恢复默认");
        }

        public bool ImportBackgroundImage(string sourcePath, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(sourcePath) || !System.IO.File.Exists(sourcePath))
            {
                error = "图片文件不存在。";
                return false;
            }
            string extension = System.IO.Path.GetExtension(sourcePath);
            if (!IsSupportedImageExtension(extension))
            {
                error = "不支持的图片格式，请选择 png/jpg/jpeg/bmp。";
                return false;
            }

            string tempFile = null;
            try
            {
                BitmapSource frame;
                using (System.IO.FileStream stream = new System.IO.FileStream(
                    sourcePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) throw new Exception("图片没有可用帧。");
                    frame = decoder.Frames[0];
                    if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0) throw new Exception("图片尺寸无效。");
                }

                string backgroundDir = _store.Paths.BackgroundDir;
                System.IO.Directory.CreateDirectory(backgroundDir);
                string targetFile = System.IO.Path.Combine(backgroundDir, "bg.png");
                tempFile = targetFile + ".tmp";
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                using (System.IO.FileStream output = new System.IO.FileStream(
                    tempFile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                {
                    encoder.Save(output);
                }
                if (System.IO.File.Exists(targetFile)) System.IO.File.Delete(targetFile);
                System.IO.File.Move(tempFile, targetFile);

                _config.appearance.imagePath = "background\\bg.png";
                _config.appearance.type = Config.AppearanceImage;
                _config.appearance.Normalize();
                _loadedBackgroundPath = null;
                _backgroundNoticeShown = false;
                ApplyAppearance();
                SaveNow();
                Logger.Info("已导入背景图片：" + sourcePath);
                return true;
            }
            catch (Exception ex)
            {
                error = "图片导入失败：" + ex.Message;
                Logger.Error("导入背景图片失败：" + sourcePath, ex);
                if (tempFile != null && System.IO.File.Exists(tempFile))
                {
                    try { System.IO.File.Delete(tempFile); } catch { }
                }
                return false;
            }
        }

        public void ClearBackgroundImage()
        {
            _config.appearance.imagePath = "";
            _config.appearance.type = Config.AppearanceSolid;
            _config.appearance.Normalize();
            _loadedBackgroundPath = null;
            _backgroundImage.Source = null;
            _backgroundNoticeShown = false;
            ApplyAppearance();
            SaveNow();
            Logger.Info("已清除背景图片");
        }

        private static bool IsSupportedImageExtension(string extension)
        {
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyAppearance()
        {
            if (_card == null) return;
            AppearanceConfig appearance = _config.appearance;

            _baseLayer.Background = Theme.CardBackground(appearance.color, appearance.opacity);

            bool imageMode = appearance.type == Config.AppearanceImage
                && !string.IsNullOrEmpty(appearance.imagePath);
            if (imageMode)
            {
                ApplyBackgroundImage(appearance);
                bool hasImage = _backgroundImage.Source != null;
                _backgroundImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
                _tintLayer.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
                _bottomShade.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
                _backgroundImage.Opacity = appearance.imageOpacity / 100.0;
                if (appearance.imageBlur > 0)
                {
                    _backgroundImage.Effect = new BlurEffect
                    {
                        Radius = appearance.imageBlur,
                        RenderingBias = RenderingBias.Performance
                    };
                }
                else
                {
                    _backgroundImage.Effect = null;
                }
                _tintLayer.Background = CreateTintBrush(appearance.color, appearance.tintOpacity);
            }
            else
            {
                _backgroundImage.Visibility = Visibility.Collapsed;
                _tintLayer.Visibility = Visibility.Collapsed;
                _bottomShade.Visibility = Visibility.Collapsed;
                _backgroundImage.Effect = null;
            }

            _card.BorderBrush = appearance.glass ? Theme.CardRimBrush : Theme.CardBorderBrush;
            _card.Effect = appearance.glass ? Theme.CardShadow() : null;

            if (appearance.glass && !_glassFallbackLogged)
            {
                _glassFallbackLogged = true;
                Logger.Info("分层透明窗口使用内部磨砂；系统 Acrylic 与 AllowsTransparency 不兼容，按规格回退。");
            }
        }

        private void ApplyBackgroundImage(AppearanceConfig appearance)
        {
            if (string.IsNullOrEmpty(appearance.imagePath))
            {
                _backgroundImage.Source = null;
                _loadedBackgroundPath = null;
                return;
            }

            string fullPath = System.IO.Path.Combine(
                _store.Paths.DataDir,
                appearance.imagePath.Replace('/', '\\'));
            if (!System.IO.File.Exists(fullPath))
            {
                _backgroundImage.Source = null;
                _loadedBackgroundPath = null;
                if (!_backgroundNoticeShown)
                {
                    _backgroundNoticeShown = true;
                    string message = "背景图片不存在，已使用纯色背景。";
                    Logger.Warn(message + " " + fullPath);
                    ShowToast(message, true);
                }
                return;
            }

            if (string.Equals(_loadedBackgroundPath, fullPath, StringComparison.OrdinalIgnoreCase)
                && _backgroundImage.Source != null)
            {
                return;
            }

            try
            {
                BitmapImage image = new BitmapImage();
                using (System.IO.FileStream stream = new System.IO.FileStream(
                    fullPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze();
                _backgroundImage.Source = image;
                _loadedBackgroundPath = fullPath;
                _backgroundNoticeShown = false;
            }
            catch (Exception ex)
            {
                Logger.Error("背景图片解码失败：" + fullPath, ex);
                _backgroundImage.Source = null;
                _loadedBackgroundPath = null;
                if (!_backgroundNoticeShown)
                {
                    _backgroundNoticeShown = true;
                    ShowToast("背景图片解码失败，已使用纯色背景。", true);
                }
            }
        }

        private static Brush CreateTintBrush(string colorHex, int tintOpacity)
        {
            Color color = Theme.ParseColor(colorHex);
            int opacity = Config.ClampInt(tintOpacity, 0, 100);
            byte alpha = (byte)(opacity * 255 / 100);
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private void RebuildPanel()
        {
            ApplyWindowSize();
            RenderGrid();
        }

        private void ApplyWindowSize()
        {
            int columns = _config.gridColumns;
            int totalItems = Math.Max(1, _config.apps.Count + 1);
            int rows = LayoutMetrics.RowCount(totalItems, columns);
            bool withLabels = _config.apps.Count > 0;

            double fullGridHeight = LayoutMetrics.GridHeight(rows, columns, withLabels);
            double chromeHeight = LayoutMetrics.ChromeHeight();
            double maxWindowHeight = SystemParameters.WorkArea.Height * 0.8;
            double maxGridHeight = Math.Max(0, maxWindowHeight - chromeHeight);
            double shownGridHeight = Math.Min(fullGridHeight, maxGridHeight);

            Width = LayoutMetrics.WindowWidth(columns);
            _gridHost.Width = LayoutMetrics.GridWidth(columns);
            _gridHost.Height = fullGridHeight;
            _gridScroll.Height = shownGridHeight;
            MaxHeight = maxWindowHeight;
            Height = chromeHeight + shownGridHeight;
            ClampToWorkArea();
        }

        private void RenderGrid()
        {
            if (_gridHost == null) return;

            _gridHost.Children.Clear();
            _gridHost.RowDefinitions.Clear();
            _tiles.Clear();

            int columns = _config.gridColumns;
            double tileSize = LayoutMetrics.TileSize(columns);
            bool withLabels = _config.apps.Count > 0;
            double itemHeight = LayoutMetrics.ItemHeight(columns, withLabels);

            int totalItems = _config.apps.Count + 1;
            int rows = Math.Max(1, (int)Math.Ceiling(totalItems / (double)columns));
            for (int i = 0; i < rows; i++)
            {
                double rowHeight = itemHeight + (i < rows - 1 ? LayoutMetrics.GridGap : 0);
                _gridHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });
            }

            bool centerRows = totalItems < columns;
            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                int rowStart = row * columns;
                int rowCount = Math.Min(columns, totalItems - rowStart);

                StackPanel rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = centerRows ? HorizontalAlignment.Center : HorizontalAlignment.Left
                };

                for (int column = 0; column < rowCount; column++)
                {
                    FrameworkElement item;
                    if (index < _config.apps.Count)
                    {
                        AppEntry app = _config.apps[index];
                        AppTile tile = new AppTile(app, columns);
                        tile.LaunchRequested += OnTileLaunchRequested;
                        tile.RefreshMissing();
                        tile.IconKey = IconCache.CacheKeyFor(app.path);
                        if (!tile.IsMissing) LoadIcon(tile);
                        if (!string.IsNullOrEmpty(_pendingAppearId)
                            && string.Equals(_pendingAppearId, app.id, StringComparison.OrdinalIgnoreCase))
                        {
                            tile.PlayAppear();
                        }
                        tile.ContextMenu = BuildTileMenu(tile);
                        _tiles[app.id] = tile;
                        item = tile;
                    }
                    else
                    {
                        item = CreateAddTile(tileSize);
                    }

                    item.Margin = new Thickness(0, 0, column < rowCount - 1 ? LayoutMetrics.GridGap : 0, 0);
                    rowPanel.Children.Add(item);
                    index++;
                }

                Grid.SetRow(rowPanel, row);
                _gridHost.Children.Add(rowPanel);
            }

            _pendingAppearId = null;
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
                VerticalAlignment = VerticalAlignment.Top,
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
            hit.MouseLeftButtonUp += delegate { AddApp(); };
            return hit;
        }

        private ContextMenu BuildPanelMenu()
        {
            ContextMenu menu = UiControls.CreateContextMenu();
            MenuItem add = UiControls.CreateMenuItem("添加应用");
            add.Click += delegate { AddApp(); };
            menu.Items.Add(add);
            return menu;
        }

        private ContextMenu BuildTileMenu(AppTile tile)
        {
            ContextMenu menu = UiControls.CreateContextMenu();

            MenuItem launch = UiControls.CreateMenuItem("启动");
            launch.Click += delegate
            {
                if (tile.IsMissing)
                {
                    ShowToast("文件不存在：" + tile.Entry.path, true);
                }
                else
                {
                    LaunchApp(tile.Entry);
                }
            };
            menu.Items.Add(launch);

            MenuItem openLocation = UiControls.CreateMenuItem("打开文件位置");
            openLocation.Click += delegate { OpenAppLocation(tile.Entry); };
            menu.Items.Add(openLocation);

            MenuItem remove = UiControls.CreateMenuItem("移除");
            remove.Click += delegate { RemoveApp(tile); };
            menu.Items.Add(remove);

            return menu;
        }

        private void OnTileLaunchRequested(object sender, EventArgs e)
        {
            AppTile tile = sender as AppTile;
            if (tile == null) return;
            if (tile.IsMissing)
            {
                ShowToast("文件不存在：" + tile.Entry.path, true);
                return;
            }
            LaunchApp(tile.Entry);
        }

        private void AddApp()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "添加应用",
                Filter = "应用程序 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd",
                FilterIndex = 1,
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };
            if (!string.IsNullOrEmpty(_config.lastBrowseDir) && System.IO.Directory.Exists(_config.lastBrowseDir))
            {
                dialog.InitialDirectory = _config.lastBrowseDir;
            }

            bool? result = dialog.ShowDialog(this);
            if (result != true) return;

            string path = dialog.FileName;
            if (!System.IO.File.Exists(path) || !LaunchCommand.IsSupportedAppFile(path))
            {
                ShowToast("不支持的文件类型：" + path, true);
                return;
            }

            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) _config.lastBrowseDir = directory;

            AppTile existing = FindTileByPath(path);
            if (existing != null || Config.ContainsPath(_config.apps, path))
            {
                if (existing != null)
                {
                    existing.BringIntoView();
                    existing.PlayAttention();
                }
                SaveNow();
                ShowToast("已添加", false);
                return;
            }

            AppEntry app = new AppEntry(Config.CreateId(), path, Config.NameFromPath(path));
            _config.apps.Add(app);
            _pendingAppearId = app.id;
            SaveNow();
            RebuildPanel();
            Logger.Info("已添加应用：" + path);
        }

        private void RemoveApp(AppTile tile)
        {
            if (tile == null || tile.Entry == null) return;
            MessageBoxResult result = MessageBox.Show(
                this,
                "确定要从面板移除「" + tile.Entry.name + "」吗？",
                "EveryGo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            string appId = tile.Entry.id;
            for (int i = _config.apps.Count - 1; i >= 0; i--)
            {
                AppEntry app = _config.apps[i];
                if (app != null && string.Equals(app.id, appId, StringComparison.OrdinalIgnoreCase))
                {
                    _config.apps.RemoveAt(i);
                }
            }
            _tiles.Remove(appId);
            SaveNow();
            Logger.Info("已移除应用：" + tile.Entry.path);
            tile.PlayDisappear(delegate { RebuildPanel(); });
        }

        private void LaunchApp(AppEntry app)
        {
            if (app == null) return;
            if (!System.IO.File.Exists(app.path))
            {
                ShowToast("文件不存在：" + app.path, true);
                return;
            }
            try
            {
                ProcessStartInfo info = LaunchCommand.CreateStartInfo(app.path, _config.consoleMode);
                Process.Start(info);
                Logger.Info("启动应用：" + app.path);
            }
            catch (Exception ex)
            {
                Logger.Error("启动应用失败：" + app.path, ex);
                ShowToast("启动失败：" + app.name, true);
            }
        }

        private void OpenAppLocation(AppEntry app)
        {
            if (app == null) return;
            if (!System.IO.File.Exists(app.path))
            {
                ShowToast("文件不存在：" + app.path, true);
                return;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "explorer.exe";
                info.Arguments = "/select,\"" + app.path + "\"";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Logger.Error("打开文件位置失败：" + app.path, ex);
                ShowToast("无法打开文件位置：" + app.name, true);
            }
        }

        private AppTile FindTile(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;
            AppTile tile;
            return _tiles.TryGetValue(appId, out tile) ? tile : null;
        }

        private AppTile FindTileByPath(string path)
        {
            string target = Config.NormalizePathForCompare(path);
            foreach (AppTile tile in _tiles.Values)
            {
                if (tile == null || tile.Entry == null) continue;
                if (string.Equals(Config.NormalizePathForCompare(tile.Entry.path), target, StringComparison.OrdinalIgnoreCase))
                {
                    return tile;
                }
            }
            return null;
        }

        private void LoadIcon(AppTile tile)
        {
            if (tile == null || tile.Entry == null) return;
            string appId = tile.Entry.id;
            string appName = tile.Entry.name;
            IconCache.LoadAsync(tile.Entry.path, delegate(BitmapSource icon, bool usedFallback)
            {
                AppTile current = FindTile(appId);
                if (current == null) return;
                current.SetIcon(icon);
                if (usedFallback && !current.IsMissing && !_iconFallbackNotified.Contains(appId))
                {
                    _iconFallbackNotified.Add(appId);
                    ShowToast("无法提取图标：" + appName + "，已使用通用图标", true);
                }
            });
        }

        private void RefreshTiles()
        {
            if (_tiles.Count == 0) return;
            foreach (AppEntry app in _config.apps)
            {
                if (app == null) continue;
                AppTile tile = FindTile(app.id);
                if (tile == null) continue;
                tile.RefreshMissing();
                string key = IconCache.CacheKeyFor(app.path);
                if (!tile.IsMissing && !string.Equals(tile.IconKey, key, StringComparison.Ordinal))
                {
                    tile.IconKey = key;
                    LoadIcon(tile);
                }
            }
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
            RebuildPanel();
            ScheduleSave();
        }

        public void SetConsoleMode(string mode)
        {
            if (!Config.IsValidConsoleMode(mode) || mode == _config.consoleMode) return;
            _config.consoleMode = mode;
            ScheduleSave();
            Logger.Info("脚本控制台模式：" + mode);
        }

        public bool SetCloseAction(string action, out string error)
        {
            error = "";
            if (action != Config.CloseTray && action != Config.CloseExit)
            {
                error = "非法的关闭行为。";
                return false;
            }
            if (action == Config.CloseTray)
            {
                if (!EnsureTray())
                {
                    error = "托盘图标创建失败。";
                    return false;
                }
            }
            else
            {
                if (!IsVisible) ShowPanel();
                DisposeTray();
            }
            _config.closeAction = action;
            SaveNow();
            Logger.Info("关闭行为：" + action);
            return true;
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

        public void OpenSettings()
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

        public void ShowPanel()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(handle, NativeMethods.SW_SHOW);
                NativeMethods.SetForegroundWindow(handle);
            }
        }

        public void ShowNotice(string message, bool warning)
        {
            ShowToast(message, warning);
        }

        public void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            SaveNow();
            DisposeTray();
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
            }
            else
            {
                Close();
            }
        }

        private void RequestClose()
        {
            if (_config.closeAction == Config.CloseTray)
            {
                HideToTray();
            }
            else
            {
                ExitApplication();
            }
        }

        private void HideToTray()
        {
            if (_config.closeAction == Config.CloseTray)
            {
                if (!EnsureTray())
                {
                    _config.closeAction = Config.CloseExit;
                    SaveNow();
                    ShowToast("托盘图标创建失败，关闭行为已改为直接退出。", true);
                    return;
                }
            }
            SaveNow();
            Hide();
        }

        private bool EnsureTray()
        {
            if (_tray != null) return true;
            try
            {
                _tray = new TrayService(this, _store.Paths.ExePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("创建托盘图标失败", ex);
                return false;
            }
        }

        private void DisposeTray()
        {
            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }
        }

        private void ApplyTrayAtStartup()
        {
            if (_config.closeAction != Config.CloseTray) return;
            if (EnsureTray()) return;

            _config.closeAction = Config.CloseExit;
            try
            {
                _store.Save(_config);
            }
            catch (Exception ex)
            {
                Logger.Error("托盘失败后保存配置失败", ex);
            }
            AppendStartupNotice("托盘图标创建失败，关闭行为已改为直接退出。");
        }

        private void AppendStartupNotice(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _startupNotice = string.IsNullOrEmpty(_startupNotice) ? message : _startupNotice + " " + message;
        }

        private void ToggleTopmost()
        {
            ApplyTopmost(!_config.window.topmost);
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
            if (!string.IsNullOrEmpty(_startupNotice))
            {
                ShowToast(_startupNotice, true);
            }

            // 启动完成后再预热菜单/滑杆/滚动条样式，首次右键与设置窗口不闪白。
            Dispatcher.BeginInvoke(new Action(delegate
            {
                UiControls.MenuItemStyle();
                UiControls.ContextMenuStyle();
                AppearanceStyles.SliderStyle();
                AppearanceStyles.ScrollBarStyle();
            }), DispatcherPriority.ApplicationIdle);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_exiting) return;
            if (_config.closeAction == Config.CloseTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            SaveNow();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (_saveTimer != null) _saveTimer.Stop();
            if (_toastTimer != null) _toastTimer.Stop();
            if (_iconRefreshTimer != null) _iconRefreshTimer.Stop();
            DisposeTray();
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
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None
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
            if (!double.IsNaN(Width))
            {
                _toast.MaxWidth = Math.Max(220, Width - 60);
            }
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
