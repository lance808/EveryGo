using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace EveryGo
{
    /// <summary>偏实深色磨砂的统一视觉常量。</summary>
    internal static class Theme
    {
        public const double CardRadius = 18;

        public static readonly Color AccentColor = Color.FromRgb(0x7F, 0xB2, 0xFF);
        public static readonly Brush AccentBrush = Frozen(new SolidColorBrush(AccentColor));
        public static readonly Brush TextPrimaryBrush = Frozen(new SolidColorBrush(Color.FromArgb(230, 0xE8, 0xEA, 0xF0)));
        public static readonly Brush TextSecondaryBrush = Frozen(new SolidColorBrush(Color.FromArgb(132, 0xE8, 0xEA, 0xF0)));
        public static readonly Brush TextMutedBrush = Frozen(new SolidColorBrush(Color.FromArgb(96, 0xE8, 0xEA, 0xF0)));
        public static readonly Brush IconBrush = Frozen(new SolidColorBrush(Colors.White));

        public static readonly Brush CardBorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)));
        /// <summary>玻璃描边：左上亮、右下暗，模拟顶光和侧光。</summary>
        public static readonly Brush CardRimBrush = CreateCardRimBrush();
        public static readonly Brush DividerBrush = Frozen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));

        public static readonly Brush TileBackgroundBrush = Frozen(new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)));
        public static readonly Brush TileHoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)));
        public static readonly Brush PlusHoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)));
        public static readonly Brush PlusBorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)));
        public static readonly Brush HandleDotBrush = Frozen(new SolidColorBrush(Color.FromArgb(89, 255, 255, 255)));

        public static readonly Brush ToggleOffBrush = Frozen(new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)));
        public static readonly Brush KnobBrush = Frozen(new SolidColorBrush(Color.FromArgb(245, 0xF2, 0xF5, 0xFA)));
        public static readonly Brush PillTrackBrush = Frozen(new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)));
        public static readonly Brush PillActiveBrush = Frozen(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)));
        public static readonly Brush PillHoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));
        public static readonly Brush ToastBackgroundBrush = Frozen(new SolidColorBrush(Color.FromArgb(240, 0x18, 0x1B, 0x22)));

        public static readonly FontFamily Font =
            new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");

        public static DropShadowEffect CardShadow()
        {
            // 阴影整体下移：顶部只留极窄过渡，避免白色桌面上出现宽灰边。
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 9,
                Direction = 90,
                Opacity = 0.30,
                RenderingBias = RenderingBias.Performance
            };
        }

        /// <summary>由配置颜色 + 透明度生成卡片纵向渐变（保持 82% 时偏实）。</summary>
        public static Brush CardBackground(string hexColor, int opacityPercent)
        {
            Color baseColor = ParseColor(hexColor);
            int opacity = Config.ClampInt(opacityPercent, 0, 100);
            byte alpha = (byte)(opacity * 255 / 100);

            Color top = Color.FromArgb(alpha,
                ClampByte(baseColor.R + 8),
                ClampByte(baseColor.G + 8),
                ClampByte(baseColor.B + 8));
            Color bottom = Color.FromArgb(alpha,
                ClampByte(baseColor.R - 5),
                ClampByte(baseColor.G - 5),
                ClampByte(baseColor.B - 5));

            LinearGradientBrush brush = new LinearGradientBrush(top, bottom, 90);
            brush.Freeze();
            return brush;
        }

        public static Color ParseColor(string hexColor)
        {
            if (!Config.IsValidHexColor(hexColor)) return ParseColor(Config.DefaultColor);
            return Color.FromRgb(
                Convert.ToByte(hexColor.Substring(1, 2), 16),
                Convert.ToByte(hexColor.Substring(3, 2), 16),
                Convert.ToByte(hexColor.Substring(5, 2), 16));
        }

        private static Brush CreateCardRimBrush()
        {
            LinearGradientBrush brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(84, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(48, 255, 255, 255), 0.30));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(26, 255, 255, 255), 0.62));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(22, 255, 255, 255), 1.0));
            brush.Freeze();
            return brush;
        }

        private static byte ClampByte(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        private static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
