using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EveryGo
{
    internal sealed class SettingsWindow : Window
    {
        private readonly MainWindow _main;
        private readonly Config _config;
        private Border _card;
        private string _autostartLoadError;
        private bool _suppressAutostart;

        public SettingsWindow(MainWindow main)
        {
            if (main == null) throw new ArgumentNullException("main");
            _main = main;
            _config = main.Config;

            InitializeWindow();
            BuildUi();
            PlaceNearMain();
            Loaded += delegate
            {
                if (!string.IsNullOrEmpty(_autostartLoadError))
                {
                    _main.ShowNotice("读取开机自启状态失败：" + _autostartLoadError, true);
                }
            };
        }

        private void InitializeWindow()
        {
            Title = "EveryGo 设置";
            Owner = _main;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Width = 380;
            Height = 460;
            Background = Brushes.Transparent;
            FontFamily = Theme.Font;
            UseLayoutRounding = true;
            SnapsToDevicePixels = false;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        }

        private void BuildUi()
        {
            Grid root = new Grid { Background = Brushes.Transparent };

            _card = new Border
            {
                Margin = new Thickness(18),
                CornerRadius = new CornerRadius(Theme.CardRadius),
                BorderThickness = new Thickness(1),
                BorderBrush = Theme.CardRimBrush,
                Background = Theme.CardBackground(Config.DefaultColor, Config.DefaultOpacity),
                Effect = Theme.CardShadow()
            };

            Grid cardContent = new Grid();

            Grid content = new Grid { Margin = new Thickness(16, 6, 16, 14) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            content.Children.Add(BuildHeader());

            StackPanel body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            body.Children.Add(UiControls.SectionLabel("启动"));

            bool autostartOn = false;
            string autostartError = "";
            Autostart.TryIsEnabled(out autostartOn, out autostartError);
            _autostartLoadError = autostartError;

            CheckBox autostartBox = null;
            autostartBox = UiControls.ToggleSwitch(autostartOn, delegate(bool on)
            {
                if (_suppressAutostart) return;
                string error = "";
                if (!Autostart.TrySetEnabled(on, _main.ExePath, out error))
                {
                    _suppressAutostart = true;
                    autostartBox.IsChecked = !on;
                    _suppressAutostart = false;
                    _main.ShowNotice("开机自启设置失败：" + error, true);
                }
            });
            body.Children.Add(UiControls.SettingRow("开机自启", autostartBox));
            body.Children.Add(UiControls.SettingRow(
                "窗口置顶",
                UiControls.ToggleSwitch(_config.window.topmost, delegate(bool on) { _main.ApplyTopmost(on); })));
            body.Children.Add(UiControls.Gap(8));

            body.Children.Add(UiControls.SectionLabel("行为"));
            body.Children.Add(UiControls.SettingRow(
                "关闭按钮",
                UiControls.PillGroup(
                    new string[] { "直接退出", "最小化到托盘" },
                    _config.closeAction == Config.CloseTray ? 1 : 0,
                    delegate(int index)
                    {
                        string action = index == 1 ? Config.CloseTray : Config.CloseExit;
                        string error = "";
                        if (!_main.SetCloseAction(action, out error))
                        {
                            _main.ShowNotice("切换关闭行为失败：" + error, true);
                        }
                    })));
            body.Children.Add(UiControls.SettingRow(
                "脚本控制台",
                UiControls.PillGroup(
                    new string[] { "显示", "最小化", "隐藏" },
                    ConsoleModeIndex(_config.consoleMode),
                    delegate(int index)
                    {
                        string[] modes = new string[]
                        {
                            Config.ConsoleShow,
                            Config.ConsoleMinimized,
                            Config.ConsoleHidden
                        };
                        _main.SetConsoleMode(modes[index]);
                    })));
            body.Children.Add(UiControls.Gap(8));

            body.Children.Add(UiControls.SectionLabel("面板"));
            body.Children.Add(UiControls.SettingRow(
                "网格列数",
                UiControls.PillGroup(
                    new string[] { "4 列", "5 列", "6 列" },
                    Config.IndexOfGridColumnOption(_config.gridColumns),
                    delegate(int index) { _main.SetGridColumns(Config.GridColumnOptions[index]); })));
            body.Children.Add(UiControls.Gap(8));

            body.Children.Add(UiControls.SectionLabel("数据"));
            body.Children.Add(new TextBlock
            {
                Text = "数据目录",
                FontSize = 11,
                Foreground = Theme.TextSecondaryBrush
            });
            TextBlock dataPath = new TextBlock
            {
                Text = _main.DataDirectory,
                FontSize = 10.5,
                Foreground = Theme.TextMutedBrush,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 4),
                ToolTip = UiControls.MakeToolTip(_main.DataDirectory)
            };
            body.Children.Add(dataPath);
            body.Children.Add(UiControls.LinkButton("打开数据目录", OpenDataDirectory));

            ScrollViewer scroller = new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Focusable = false,
                Background = Brushes.Transparent
            };
            Grid.SetRow(scroller, 1);
            content.Children.Add(scroller);
            content.Children.Add(BuildFooter());

            cardContent.Children.Add(content);
            _card.Child = cardContent;
            _card.MouseLeftButtonDown += OnCardMouseDown;
            root.Children.Add(_card);
            Content = root;
        }

        private static int ConsoleModeIndex(string mode)
        {
            if (mode == Config.ConsoleShow) return 0;
            if (mode == Config.ConsoleHidden) return 2;
            return 1;
        }

        private FrameworkElement BuildHeader()
        {
            Grid header = new Grid { Height = 28 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "设置",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(title);

            ToolbarIcon close = new ToolbarIcon("关闭设置", IconKind.Close);
            close.MouseLeftButtonUp += delegate { Close(); };
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            return header;
        }

        private FrameworkElement BuildFooter()
        {
            Grid footer = new Grid { Height = 28, Margin = new Thickness(0, 8, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock version = new TextBlock
            {
                Text = "v" + Program.AppVersion + " · " + Program.PhaseLabel,
                FontSize = 10.5,
                Foreground = Theme.TextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Children.Add(version);

            FrameworkElement closeButton = UiControls.SmallButton("关闭设置", Close);
            Grid.SetColumn(closeButton, 1);
            footer.Children.Add(closeButton);
            return footer;
        }

        private void OpenDataDirectory()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = _main.DataDirectory;
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Logger.Error("打开数据目录失败", ex);
                MessageBox.Show(this, "无法打开数据目录：" + ex.Message, "EveryGo", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            }
        }

        private void PlaceNearMain()
        {
            Rect area = SystemParameters.WorkArea;
            double left = _main.Left + _main.Width + 12;
            double top = _main.Top;

            if (left + Width > area.Right - 8)
            {
                left = _main.Left - Width - 12;
                if (left < area.Left + 8)
                {
                    left = area.Left + (area.Width - Width) / 2;
                }
            }
            if (top + Height > area.Bottom - 8) top = area.Bottom - Height - 8;
            if (top < area.Top + 8) top = area.Top + 8;

            Left = left;
            Top = top;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _main.SaveNow();
            base.OnClosing(e);
        }
    }
}
