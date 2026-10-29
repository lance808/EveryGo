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

        public static double WindowHeight(int rows, int columns)
        {
            double gridHeight = GridHeight(rows, TileSize(columns));
            return WindowShadowMargin * 2
                + CardBorderWidth * 2
                + ContentTopPadding
                + ToolbarHeight
                + ToolbarToGridGap
                + gridHeight
                + ContentBottomPadding;
        }

        public static int RowCount(int appCount, int columns)
        {
            int count = Math.Max(0, appCount);
            if (count == 0) return 1;
            return (int)Math.Ceiling(count / (double)columns);
        }
    }
}
