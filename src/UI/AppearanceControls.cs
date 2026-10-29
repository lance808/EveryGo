using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EveryGo
{
    /// <summary>可编程选中项的胶囊选择器（用于「纯色 / 图片」）。</summary>
    internal sealed class PillSelector
    {
        private readonly Border[] _pills;
        private readonly TextBlock[] _texts;
        private readonly Action<int> _onSelect;
        private int _selected;

        public Border Element { get; private set; }

        public PillSelector(string[] labels, int selectedIndex, Action<int> onSelect)
        {
            _onSelect = onSelect;
            _selected = selectedIndex;
            _pills = new Border[labels.Length];
            _texts = new TextBlock[labels.Length];

            Border track = new Border
            {
                Background = Theme.PillTrackBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(2),
                Tag = "interactive",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal };
            track.Child = panel;

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                TextBlock text = new TextBlock
                {
                    Text = labels[i],
                    FontSize = 11,
                    Foreground = Theme.TextSecondaryBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Border pill = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 4, 9, 4),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    Child = text,
                    Tag = "interactive"
                };
                pill.MouseLeftButtonUp += delegate
                {
                    if (_selected == index) return;
                    _selected = index;
                    Refresh();
                    if (_onSelect != null) _onSelect(index);
                };
                pill.MouseEnter += delegate
                {
                    if (_selected != index) pill.Background = Theme.PillHoverBrush;
                };
                pill.MouseLeave += delegate
                {
                    if (_selected != index) pill.Background = Brushes.Transparent;
                };
                _pills[i] = pill;
                _texts[i] = text;
                panel.Children.Add(pill);
            }

            Element = track;
            Refresh();
        }

        public int SelectedIndex { get { return _selected; } }

        public void SetSelected(int index)
        {
            if (index < 0 || index >= _pills.Length) return;
            _selected = index;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _pills.Length; i++)
            {
                bool active = i == _selected;
                _pills[i].Background = active ? Theme.PillActiveBrush : Brushes.Transparent;
                _texts[i].Foreground = active ? Theme.TextPrimaryBrush : Theme.TextSecondaryBrush;
            }
        }
    }

    /// <summary>深色滑杆行：标题 + 当前值 + 已填充的强调色轨道。</summary>
    internal sealed class SliderRow : Grid
    {
        private readonly string _suffix;

        public Slider SliderControl { get; private set; }
        public TextBlock ValueText { get; private set; }

        public SliderRow(string label, double min, double max, double value, string suffix, Action<int> onChange)
        {
            _suffix = suffix == null ? "" : suffix;
            Height = 38;
            Margin = new Thickness(0, 1, 0, 1);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid { Height = 16 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock labelText = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Theme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(labelText);

            ValueText = new TextBlock
            {
                FontSize = 10.5,
                Foreground = Theme.TextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(ValueText, 1);
            header.Children.Add(ValueText);
            Children.Add(header);

            SliderControl = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                Height = 18,
                Margin = new Thickness(0, 2, 0, 0),
                IsMoveToPointEnabled = true,
                SmallChange = 1,
                LargeChange = 10,
                Focusable = false,
                Style = AppearanceStyles.SliderStyle()
            };
            Grid.SetRow(SliderControl, 1);
            Children.Add(SliderControl);

            UpdateValueText(SliderControl.Value);
            SliderControl.ValueChanged += delegate
            {
                UpdateValueText(SliderControl.Value);
                if (onChange != null) onChange((int)Math.Round(SliderControl.Value));
            };
        }

        public void SetValue(double value)
        {
            SliderControl.Value = Math.Max(SliderControl.Minimum, Math.Min(SliderControl.Maximum, value));
            UpdateValueText(SliderControl.Value);
        }

        private void UpdateValueText(double value)
        {
            ValueText.Text = ((int)Math.Round(value)).ToString() + _suffix;
        }
    }

    /// <summary>深色滑杆与细滚动条的 XAML 模板。</summary>
    internal static class AppearanceStyles
    {
        private static Style _sliderStyle;
        private static Style _scrollBarStyle;
        private static readonly object StyleLock = new object();

        public static Style SliderStyle()
        {
            if (_sliderStyle != null) return _sliderStyle;
            lock (StyleLock)
            {
                if (_sliderStyle != null) return _sliderStyle;
                try
                {
                    string xaml =
                        @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
                        + @"xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>"
                        + @"<Setter Property='Template'>"
                        + @"<Setter.Value>"
                        + @"<ControlTemplate TargetType='{x:Type Slider}'>"
                        + @"<Grid Margin='0,3,0,3'>"
                        + @"<Border Height='4' CornerRadius='2' Background='#1FFFFFFF' VerticalAlignment='Center'/>"
                        + @"<Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' "
                        + @"Maximum='{TemplateBinding Maximum}' Minimum='{TemplateBinding Minimum}' "
                        + @"Value='{TemplateBinding Value}'>"
                        + @"<Track.DecreaseRepeatButton>"
                        + @"<RepeatButton Focusable='False'>"
                        + @"<RepeatButton.Template>"
                        + @"<ControlTemplate TargetType='{x:Type RepeatButton}'>"
                        + @"<Border Height='4' CornerRadius='2' Background='#7FB2FF' VerticalAlignment='Center'/>"
                        + @"</ControlTemplate>"
                        + @"</RepeatButton.Template>"
                        + @"</RepeatButton>"
                        + @"</Track.DecreaseRepeatButton>"
                        + @"<Track.Thumb>"
                        + @"<Thumb Width='12' Height='12' Focusable='False'>"
                        + @"<Thumb.Template>"
                        + @"<ControlTemplate TargetType='{x:Type Thumb}'>"
                        + @"<Ellipse Fill='#7FB2FF'/>"
                        + @"</ControlTemplate>"
                        + @"</Thumb.Template>"
                        + @"</Thumb>"
                        + @"</Track.Thumb>"
                        + @"<Track.IncreaseRepeatButton>"
                        + @"<RepeatButton Focusable='False' Opacity='0'/>"
                        + @"</Track.IncreaseRepeatButton>"
                        + @"</Track>"
                        + @"</Grid>"
                        + @"</ControlTemplate>"
                        + @"</Setter.Value>"
                        + @"</Setter>"
                        + @"</Style>";
                    _sliderStyle = (Style)XamlReader.Parse(xaml);
                }
                catch (Exception ex)
                {
                    Logger.Error("创建深色滑杆样式失败", ex);
                }
                return _sliderStyle;
            }
        }

        public static Style ScrollBarStyle()
        {
            if (_scrollBarStyle != null) return _scrollBarStyle;
            lock (StyleLock)
            {
                if (_scrollBarStyle != null) return _scrollBarStyle;
                try
                {
                    string xaml =
                        @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
                        + @"xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>"
                        + @"<Setter Property='Width' Value='6'/>"
                        + @"<Setter Property='Background' Value='Transparent'/>"
                        + @"<Setter Property='Template'>"
                        + @"<Setter.Value>"
                        + @"<ControlTemplate TargetType='{x:Type ScrollBar}'>"
                        + @"<Grid Width='6' Background='Transparent'>"
                        + @"<Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' "
                        + @"Maximum='{TemplateBinding Maximum}' Minimum='{TemplateBinding Minimum}' "
                        + @"Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>"
                        + @"<Track.DecreaseRepeatButton><RepeatButton Focusable='False' Opacity='0'/></Track.DecreaseRepeatButton>"
                        + @"<Track.Thumb>"
                        + @"<Thumb Width='4' MinHeight='24' Focusable='False'>"
                        + @"<Thumb.Template>"
                        + @"<ControlTemplate TargetType='{x:Type Thumb}'>"
                        + @"<Border Background='#30FFFFFF' CornerRadius='2'/>"
                        + @"</ControlTemplate>"
                        + @"</Thumb.Template>"
                        + @"</Thumb>"
                        + @"</Track.Thumb>"
                        + @"<Track.IncreaseRepeatButton><RepeatButton Focusable='False' Opacity='0'/></Track.IncreaseRepeatButton>"
                        + @"</Track>"
                        + @"</Grid>"
                        + @"</ControlTemplate>"
                        + @"</Setter.Value>"
                        + @"</Setter>"
                        + @"</Style>";
                    _scrollBarStyle = (Style)XamlReader.Parse(xaml);
                }
                catch (Exception ex)
                {
                    Logger.Error("创建深色滚动条样式失败", ex);
                }
                return _scrollBarStyle;
            }
        }
    }
}
