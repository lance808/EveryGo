using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EveryGo
{
    internal sealed class SettingsWindow : Window
    {
        private readonly MainWindow _main;
        private readonly Config _config;
        private Border _card;

        private PillSelector _typeSelector;
        private StackPanel _solidPanel;
        private StackPanel _imagePanel;
        private Border _colorSwatch;
        private TextBlock _colorText;
        private TextBlock _imageNameText;
        private CheckBox _glassToggle;
        private SliderRow _opacityRow;
        private SliderRow _imageOpacityRow;
        private SliderRow _imageBlurRow;
        private SliderRow _tintRow;

        private bool _suppressUi;
        private bool _lowOpacityWarned;
        private string _autostartLoadError;

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
            Height = 560;
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
                if (_suppressUi) return;
                string error = "";
                if (!Autostart.TrySetEnabled(on, _main.ExePath, out error))
                {
                    _suppressUi = true;
                    autostartBox.IsChecked = !on;
                    _suppressUi = false;
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

            body.Children.Add(UiControls.SectionLabel("背景"));
            _typeSelector = new PillSelector(
                new string[] { "纯色", "图片" },
                _config.appearance.type == Config.AppearanceImage ? 1 : 0,
                OnAppearanceTypeSelected);
            body.Children.Add(UiControls.SettingRow("背景类型", _typeSelector.Element));

            _solidPanel = new StackPanel();
            _solidPanel.Children.Add(BuildColorRow());
            _opacityRow = new SliderRow(
                "透明度", 0, 100, _config.appearance.opacity, "%", OnOpacityChanged);
            _solidPanel.Children.Add(_opacityRow);
            body.Children.Add(_solidPanel);

            _imagePanel = new StackPanel();
            _imagePanel.Children.Add(BuildImageActionsRow());
            _imageNameText = new TextBlock
            {
                FontSize = 10,
                Foreground = Theme.TextMutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _imagePanel.Children.Add(_imageNameText);
            _imageOpacityRow = new SliderRow(
                "图片不透明度", 0, 100, _config.appearance.imageOpacity, "%", OnImageOpacityChanged);
            _imageBlurRow = new SliderRow(
                "图片模糊", 0, 24, _config.appearance.imageBlur, "px", OnImageBlurChanged);
            _tintRow = new SliderRow(
                "色调强度", 0, 100, _config.appearance.tintOpacity, "%", OnTintOpacityChanged);
            _imagePanel.Children.Add(_imageOpacityRow);
            _imagePanel.Children.Add(_imageBlurRow);
            _imagePanel.Children.Add(_tintRow);
            body.Children.Add(_imagePanel);
            body.Children.Add(UiControls.Gap(8));

            body.Children.Add(UiControls.SectionLabel("玻璃"));
            _glassToggle = UiControls.ToggleSwitch(_config.appearance.glass, delegate(bool on)
            {
                if (_suppressUi) return;
                _main.UpdateAppearance(delegate(AppearanceConfig appearance) { appearance.glass = on; });
            });
            body.Children.Add(UiControls.SettingRow("玻璃质感", _glassToggle));
            FrameworkElement reset = UiControls.LinkButton("恢复默认外观", RestoreDefaultAppearance);
            reset.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(reset);
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
            Style scrollBarStyle = AppearanceStyles.ScrollBarStyle();
            if (scrollBarStyle != null)
            {
                scroller.Resources[typeof(ScrollBar)] = scrollBarStyle;
            }
            Grid.SetRow(scroller, 1);
            content.Children.Add(scroller);
            content.Children.Add(BuildFooter());

            cardContent.Children.Add(content);
            _card.Child = cardContent;
            _card.MouseLeftButtonDown += OnCardMouseDown;
            root.Children.Add(_card);
            Content = root;

            UpdateColorUi();
            UpdateImageName();
            UpdateAppearancePanels();
        }

        private FrameworkElement BuildColorRow()
        {
            Grid row = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = "颜色",
                FontSize = 12,
                Foreground = Theme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(label);

            StackPanel right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _colorSwatch = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(5),
                BorderBrush = Theme.CardBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _colorText = new TextBlock
            {
                FontSize = 11,
                Foreground = Theme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            right.Children.Add(_colorSwatch);
            right.Children.Add(_colorText);
            right.Children.Add(UiControls.LinkButton("更改", ChooseColor));
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            return row;
        }

        private FrameworkElement BuildImageActionsRow()
        {
            Grid row = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = "背景图片",
                FontSize = 12,
                Foreground = Theme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(label);

            StackPanel right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            FrameworkElement choose = UiControls.LinkButton("选择", ChooseImageAction);
            choose.Margin = new Thickness(0, 0, 10, 0);
            right.Children.Add(choose);
            right.Children.Add(UiControls.LinkButton("清除", ClearImageAction));
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            return row;
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

            ToolbarIcon close = new ToolbarIcon("关闭", IconKind.Close);
            close.VerticalAlignment = VerticalAlignment.Center;
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
                Text = "EveryGo v" + Program.AppVersion,
                FontSize = 10.5,
                Foreground = Theme.TextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Children.Add(version);
            return footer;
        }

        private void OnAppearanceTypeSelected(int index)
        {
            if (_suppressUi) return;
            if (index == 0)
            {
                _main.UpdateAppearance(delegate(AppearanceConfig appearance)
                {
                    appearance.type = Config.AppearanceSolid;
                });
                UpdateAppearancePanels();
                return;
            }

            bool hasImage = !string.IsNullOrEmpty(_config.appearance.imagePath)
                && System.IO.File.Exists(System.IO.Path.Combine(
                    _main.DataDirectory,
                    _config.appearance.imagePath.Replace('/', '\\')));
            if (!hasImage)
            {
                if (!ChooseImage())
                {
                    _typeSelector.SetSelected(0);
                    UpdateAppearancePanels();
                }
                return;
            }

            _main.UpdateAppearance(delegate(AppearanceConfig appearance)
            {
                appearance.type = Config.AppearanceImage;
            });
            UpdateAppearancePanels();
        }

        private bool ChooseImage()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                FilterIndex = 1,
                CheckFileExists = true,
                Multiselect = false
            };
            bool? result = dialog.ShowDialog(this);
            if (result != true) return false;

            string error = "";
            if (!_main.ImportBackgroundImage(dialog.FileName, out error))
            {
                _main.ShowNotice(error, true);
                return false;
            }
            _typeSelector.SetSelected(1);
            UpdateImageName();
            UpdateAppearancePanels();
            return true;
        }

        private void ChooseImageAction()
        {
            ChooseImage();
        }

        private void ClearImageAction()
        {
            _main.ClearBackgroundImage();
            _typeSelector.SetSelected(0);
            UpdateImageName();
            UpdateAppearancePanels();
        }

        private void ChooseColor()
        {
            Color current = Theme.ParseColor(_config.appearance.color);
            System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog();
            dialog.FullOpen = true;
            dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string hex = string.Format(
                    "#{0:X2}{1:X2}{2:X2}",
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B);
                _main.UpdateAppearance(delegate(AppearanceConfig appearance) { appearance.color = hex; });
                UpdateColorUi();
            }
        }

        private void OnOpacityChanged(int value)
        {
            if (_suppressUi) return;
            _main.UpdateAppearance(delegate(AppearanceConfig appearance) { appearance.opacity = value; });
            if (value < 20 && !_lowOpacityWarned)
            {
                _lowOpacityWarned = true;
                _main.ShowNotice("透明度过低（" + value + "%），可能影响可读性。", true);
            }
        }

        private void OnImageOpacityChanged(int value)
        {
            if (_suppressUi) return;
            _main.UpdateAppearance(delegate(AppearanceConfig appearance) { appearance.imageOpacity = value; });
        }

        private void OnImageBlurChanged(int value)
        {
            if (_suppressUi) return;
            _main.UpdateAppearance(delegate(AppearanceConfig appearance) { appearance.imageBlur = value; });
        }

        private void OnTintOpacityChanged(int value)
        {
            if (_suppressUi) return;
            _main.UpdateAppearance(delegate(AppearanceConfig appearance) { appearance.tintOpacity = value; });
        }

        private void RestoreDefaultAppearance()
        {
            _main.ResetAppearance();
            _suppressUi = true;
            _typeSelector.SetSelected(0);
            _glassToggle.IsChecked = _config.appearance.glass;
            _opacityRow.SetValue(_config.appearance.opacity);
            _imageOpacityRow.SetValue(_config.appearance.imageOpacity);
            _imageBlurRow.SetValue(_config.appearance.imageBlur);
            _tintRow.SetValue(_config.appearance.tintOpacity);
            _suppressUi = false;
            UpdateColorUi();
            UpdateImageName();
            UpdateAppearancePanels();
        }

        private void UpdateColorUi()
        {
            if (_colorSwatch == null || _colorText == null) return;
            _colorSwatch.Background = new SolidColorBrush(Theme.ParseColor(_config.appearance.color));
            _colorText.Text = _config.appearance.color;
        }

        private void UpdateImageName()
        {
            if (_imageNameText == null) return;
            string imagePath = _config.appearance.imagePath;
            if (string.IsNullOrEmpty(imagePath))
            {
                _imageNameText.Text = "未选择图片";
            }
            else
            {
                _imageNameText.Text = System.IO.Path.GetFileName(imagePath);
            }
        }

        private void UpdateAppearancePanels()
        {
            if (_solidPanel == null || _imagePanel == null) return;
            bool imageMode = _config.appearance.type == Config.AppearanceImage;
            _solidPanel.Visibility = imageMode ? Visibility.Collapsed : Visibility.Visible;
            _imagePanel.Visibility = imageMode ? Visibility.Visible : Visibility.Collapsed;
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
