using System;

namespace EveryGo
{
    /// <summary>面板尺寸计算：默认 4×72，密度 5/6 时方块与窗口按比例收窄。</summary>
    internal static class LayoutMetrics
    {
        public const double WindowShadowMargin = 18;
        public const double CardBorderWidth = 1;
        public const double CardPaddingX = 14;
        public const double ContentTopPadding = 6;
        public const double ToolbarHeight = 28;
        public const double ToolbarToGridGap = 8;
        public const double GridGap = 10;
        public const double ContentBottomPadding = 14;
        public const double TileRadius = 14;
        public const double IconSize = 44;
        public const double LabelGap = 4;
        public const double LabelHeight = 14;

        public static double TileSize(int columns)
        {
            // 4 → 72, 5 → 64, 6 → 56
            return 72 - (columns - 4) * 8;
        }

        public static double IconSizeFor(int columns)
        {
            // 4 → 44, 5 → 40, 6 → 36
            return 44 - (columns - 4) * 4;
        }

        public static double GridWidth(int columns)
        {
            return columns * TileSize(columns) + (columns - 1) * GridGap;
        }

        public static double WindowWidth(int columns)
        {
            return WindowShadowMargin * 2
                + CardBorderWidth * 2
                + CardPaddingX * 2
                + GridWidth(columns);
        }

        public static double GridHeight(int rows, double tileSize)
        {
            int safeRows = Math.Max(1, rows);
            return safeRows * tileSize + (safeRows - 1) * GridGap;
        }

        /// <summary>含名称行的单行高度：方块 + 4px 间隙 + 14px 文字。</summary>
        public static double ItemHeight(int columns, bool withLabel)
        {
            return TileSize(columns) + (withLabel ? LabelGap + LabelHeight : 0);
        }

        public static double GridHeight(int rows, int columns, bool withLabels)
        {
            int safeRows = Math.Max(1, rows);
            return safeRows * ItemHeight(columns, withLabels) + (safeRows - 1) * GridGap;
        }

        /// <summary>卡片外框高度：窗口透明边距 + 卡片边框 + 内边距 + 工具栏 + 间隙 + 底部内边距。</summary>
        public static double ChromeHeight()
        {
            return WindowShadowMargin * 2
                + CardBorderWidth * 2
                + ContentTopPadding
                + ToolbarHeight
                + ToolbarToGridGap
                + ContentBottomPadding;
        }

        public static double WindowHeight(int rows, int columns, bool withLabels)
        {
            return ChromeHeight() + GridHeight(rows, columns, withLabels);
        }

        public static int RowCount(int appCount, int columns)
        {
            int count = Math.Max(0, appCount);
            if (count == 0) return 1;
            return (int)Math.Ceiling(count / (double)columns);
        }
    }
}
