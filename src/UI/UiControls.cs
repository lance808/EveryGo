using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace EveryGo
{
    /// <summary>工具栏图标按钮：默认 35% 不透明，悬停 90%，150ms ease-out。</summary>
    internal sealed class ToolbarIcon : Border
    {
        private const double IdleOpacity = 0.35;
        private const double HoverOpacity = 0.9;
        private bool _active;

        public ToolbarIcon(string tooltip, IconKind kind)
        {
            Width = 24;
            Height = 24;
            Background = Brushes.Transparent;
            CornerRadius = new CornerRadius(6);
            Child = IconFactory.Create(kind, 16);
            ToolTip = UiControls.MakeToolTip(tooltip);
            Cursor = Cursors.Hand;
            Tag = "interactive";
            Opacity = IdleOpacity;
            ToolTipService.SetInitialShowDelay(this, 350);
            ToolTipService.SetShowDuration(this, 5000);
        }

        public void SetActive(bool active)
        {
            _active = active;
            AnimateTo(active ? HoverOpacity : IdleOpacity);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            AnimateTo(HoverOpacity);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            AnimateTo(_active ? HoverOpacity : IdleOpacity);
        }

        private void AnimateTo(double target)
        {
            DoubleAnimation animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(150));
            animation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            animation.FillBehavior = FillBehavior.HoldEnd;
            BeginAnimation(OpacityProperty, animation);
        }
    }

    internal static class UiControls
    {
        public static ToolTip MakeToolTip(string text)
        {
            TextBlock content = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = Theme.TextPrimaryBrush,
                TextWrapping = TextWrapping.NoWrap
            };
            return new ToolTip
            {
                Content = content,
                FontFamily = Theme.Font,
                Background = Theme.ToastBackgroundBrush,
                BorderBrush = Theme.CardBorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5),
                HasDropShadow = false
            };
        }

        public static CheckBox ToggleSwitch(bool isChecked, Action<bool> onChange)
        {
            CheckBox box = new CheckBox
            {
                IsChecked = isChecked,
                Width = 38,
                Height = 22,
                Cursor = Cursors.Hand,
                Focusable = false,
                Tag = "interactive",
                VerticalAlignment = VerticalAlignment.Center
            };

            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.WidthProperty, 38.0);
            grid.SetValue(FrameworkElement.HeightProperty, 22.0);

            FrameworkElementFactory track = new FrameworkElementFactory(typeof(Border));
            track.Name = "track";
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            track.SetValue(Border.BackgroundProperty, Theme.ToggleOffBrush);

            FrameworkElementFactory knob = new FrameworkElementFactory(typeof(Border));
            knob.Name = "knob";
            knob.SetValue(FrameworkElement.WidthProperty, 18.0);
            knob.SetValue(FrameworkElement.HeightProperty, 18.0);
            knob.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            knob.SetValue(Border.BackgroundProperty, Theme.KnobBrush);
            knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            knob.SetValue(FrameworkElement.MarginProperty, new Thickness(2));

            grid.AppendChild(track);
            grid.AppendChild(knob);

            ControlTemplate template = new ControlTemplate(typeof(CheckBox));
            template.VisualTree = grid;

            Trigger trigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            Setter trackOn = new Setter();
            trackOn.Property = Border.BackgroundProperty;
            trackOn.Value = Theme.AccentBrush;
            trackOn.TargetName = "track";
            Setter knobOn = new Setter();
            knobOn.Property = FrameworkElement.HorizontalAlignmentProperty;
            knobOn.Value = HorizontalAlignment.Right;
            knobOn.TargetName = "knob";
            trigger.Setters.Add(trackOn);
            trigger.Setters.Add(knobOn);
            template.Triggers.Add(trigger);
            box.Template = template;

            if (onChange != null)
            {
                box.Checked += delegate { onChange(true); };
                box.Unchecked += delegate { onChange(false); };
            }
            return box;
        }

        public static FrameworkElement PillGroup(string[] labels, int selectedIndex, Action<int> onSelect)
        {
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

            Border[] pills = new Border[labels.Length];
            TextBlock[] texts = new TextBlock[labels.Length];
            int current = selectedIndex;

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
                    if (current == index) return;
                    current = index;
                    RefreshPills(pills, texts, current);
                    if (onSelect != null) onSelect(index);
                };
                pill.MouseEnter += delegate
                {
                    if (current != index) pill.Background = Theme.PillHoverBrush;
                };
                pill.MouseLeave += delegate
                {
                    if (current != index) pill.Background = Brushes.Transparent;
                };
                pills[i] = pill;
                texts[i] = text;
                panel.Children.Add(pill);
            }

            RefreshPills(pills, texts, current);
            return track;
        }

        private static void RefreshPills(Border[] pills, TextBlock[] texts, int current)
        {
            for (int i = 0; i < pills.Length; i++)
            {
                bool active = i == current;
                pills[i].Background = active ? Theme.PillActiveBrush : Brushes.Transparent;
                texts[i].Foreground = active ? Theme.TextPrimaryBrush : Theme.TextSecondaryBrush;
            }
        }

        public static FrameworkElement LinkButton(string label, Action onClick)
        {
            TextBlock text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Theme.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Border button = new Border
            {
                Padding = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = text,
                Tag = "interactive",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.MouseEnter += delegate { text.Opacity = 1.0; };
            button.MouseLeave += delegate { text.Opacity = 0.8; };
            text.Opacity = 0.8;
            button.MouseLeftButtonUp += delegate
            {
                if (onClick != null) onClick();
            };
            return button;
        }

        public static FrameworkElement SmallButton(string label, Action onClick)
        {
            TextBlock text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Theme.TextPrimaryBrush
            };
            Border button = new Border
            {
                Background = Theme.PillActiveBrush,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 5, 12, 5),
                Cursor = Cursors.Hand,
                Child = text,
                Tag = "interactive",
                VerticalAlignment = VerticalAlignment.Center
            };
            button.MouseEnter += delegate { button.Background = Theme.PillHoverBrush; };
            button.MouseLeave += delegate { button.Background = Theme.PillActiveBrush; };
            if (onClick != null)
            {
                button.MouseLeftButtonUp += delegate { onClick(); };
            }
            return button;
        }

        public static TextBlock SectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = Theme.TextSecondaryBrush,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        public static Grid SettingRow(string label, FrameworkElement control)
        {
            Grid row = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock text = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Theme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(text, 0);
            row.Children.Add(text);

            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                control.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(control, 1);
                row.Children.Add(control);
            }
            return row;
        }

        public static FrameworkElement Gap(double height)
        {
            return new Border { Height = height, Background = Brushes.Transparent };
        }

        public static bool IsInteractive(object source)
        {
            DependencyObject node = source as DependencyObject;
            while (node != null)
            {
                FrameworkElement element = node as FrameworkElement;
                if (element != null)
                {
                    string tag = element.Tag as string;
                    if (tag == "interactive") return true;
                }
                if (node is ButtonBase) return true;
                node = GetParent(node);
            }
            return false;
        }

        private static DependencyObject GetParent(DependencyObject node)
        {
            try
            {
                return VisualTreeHelper.GetParent(node);
            }
            catch
            {
                return null;
            }
        }
    }
}
