using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EveryGo
{
    internal enum IconKind
    {
        Pin,
        Gear,
        Minimize,
        Close,
        Plus
    }

    /// <summary>纯矢量图标，避免引入图片/字体资源。</summary>
    internal static class IconFactory
    {
        public static FrameworkElement Create(IconKind kind, double size)
        {
            Canvas canvas;
            if (kind == IconKind.Plus)
            {
                canvas = new Canvas { Width = 24, Height = 24 };
                canvas.Children.Add(StrokePath("M 12,3 L 12,21 M 3,12 L 21,12", 1.5));
            }
            else
            {
                canvas = new Canvas { Width = 16, Height = 16 };
                switch (kind)
                {
                    case IconKind.Pin:
                        canvas.Children.Add(StrokePath("M 6.1,2.4 L 9.9,2.4 L 9.2,6.0 L 12.4,8.3 L 3.6,8.3 L 6.8,6.0 Z", 1.2));
                        canvas.Children.Add(StrokePath("M 8,8.3 L 8,13.4", 1.2));
                        break;
                    case IconKind.Gear:
                        AddGear(canvas);
                        break;
                    case IconKind.Minimize:
                        canvas.Children.Add(StrokePath("M 3.8,8.4 L 12.2,8.4", 1.3));
                        break;
                    case IconKind.Close:
                        canvas.Children.Add(StrokePath("M 4.2,4.2 L 11.8,11.8 M 11.8,4.2 L 4.2,11.8", 1.3));
                        break;
                }
            }

            Viewbox viewbox = new Viewbox
            {
                Width = size,
                Height = size,
                Child = canvas,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return viewbox;
        }

        private static void AddGear(Canvas canvas)
        {
            Geometry gear = CreateGearGeometry(8, 6.9, 4.9);
            EllipseGeometry hole = new EllipseGeometry(new Point(8, 8), 2.35, 2.35);
            GeometryGroup group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(gear);
            group.Children.Add(hole);
            Path path = new Path { Data = group, Fill = Theme.IconBrush };
            canvas.Children.Add(path);
        }

        private static Geometry CreateGearGeometry(double center, double outer, double inner)
        {
            const int teeth = 6;
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                Point first = new Point(0, 0);
                for (int i = 0; i < teeth * 2; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / teeth;
                    double radius = (i % 2 == 0) ? outer : inner;
                    Point point = new Point(
                        center + radius * Math.Cos(angle),
                        center + radius * Math.Sin(angle));
                    if (i == 0)
                    {
                        context.BeginFigure(point, true, true);
                        first = point;
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                }
                context.LineTo(first, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Path StrokePath(string data, double thickness)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Stroke = Theme.IconBrush,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }
    }
}
