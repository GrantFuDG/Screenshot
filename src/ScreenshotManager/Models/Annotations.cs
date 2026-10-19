using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// WinForms/Drawing implicit usings make these names ambiguous; pin them to WPF.
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;

namespace ScreenshotManager.Models
{
    /// <summary>Tools available in the capture overlay toolbar.</summary>
    public enum ToolKind
    {
        None,
        Rectangle,
        Ellipse,
        Line,
        Arrow,
        Pen,
        Highlighter,
        Text,
        Mosaic,
        Blur,
        Eraser
    }

    public enum ShapeKind { Rectangle, Ellipse, Mosaic, Blur }

    public static class Fonts
    {
        public static readonly Typeface Ui = new Typeface(
            new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Arial"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        public static readonly Typeface UiBold = new Typeface(
            new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Arial"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public static readonly Typeface Mono = new Typeface(
            new FontFamily("Consolas, Cascadia Mono, Courier New"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    public static class Geom
    {
        public static Rect Normalize(Point a, Point b)
        {
            return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        public static Rect ToRect(System.Drawing.Rectangle r) => new Rect(r.X, r.Y, r.Width, r.Height);

        public static System.Drawing.Rectangle ToGdi(Rect r) =>
            System.Drawing.Rectangle.FromLTRB((int)Math.Round(r.Left), (int)Math.Round(r.Top),
                                              (int)Math.Round(r.Right), (int)Math.Round(r.Bottom));

        public static Rect Inflate(Rect r, double d) =>
            new Rect(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);

        public static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-9) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            double px = a.X + t * dx, py = a.Y + t * dy;
            return Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
        }
    }

    /// <summary>Base class for every drawn annotation. Coordinates are GLOBAL physical screen pixels.</summary>
    public abstract class Annotation
    {
        public Color Color = Colors.Red;
        public double Thickness = 2;

        public abstract Rect Bounds { get; }
        public abstract void Render(DrawingContext dc, double pixelsPerDip);
        public abstract bool HitTest(Point p, double tolerance);
        public abstract Annotation Clone();

        protected static Brush BrushOf(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        protected static Pen PenOf(Color c, double thickness, bool round = false)
        {
            var p = new Pen(BrushOf(c), Math.Max(0.5, thickness))
            {
                StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
                EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
                LineJoin = round ? PenLineJoin.Round : PenLineJoin.Miter
            };
            p.Freeze();
            return p;
        }
    }

    /// <summary>Rectangle / ellipse / mosaic / blur.</summary>
    public sealed class RectAnnotation : Annotation
    {
        public ShapeKind Kind = ShapeKind.Rectangle;
        public Rect Area;
        public int BlockSize = 12;
        /// <summary>Pre-rendered mosaic/blur patch, exactly Area-sized (built once, outside the render pass).</summary>
        public BitmapSource Patch;

        public override Rect Bounds => Area;

        public override void Render(DrawingContext dc, double pixelsPerDip)
        {
            var area = Area;
            if (area.Width < 1 || area.Height < 1) return;
            switch (Kind)
            {
                case ShapeKind.Rectangle:
                    dc.DrawRectangle(null, PenOf(Color, Thickness), area);
                    break;
                case ShapeKind.Ellipse:
                    dc.DrawEllipse(null, PenOf(Color, Thickness), new Point(area.X + area.Width / 2, area.Y + area.Height / 2),
                        area.Width / 2, area.Height / 2);
                    break;
                case ShapeKind.Mosaic:
                case ShapeKind.Blur:
                    if (Patch != null) dc.DrawImage(Patch, area);
                    break;
            }
        }

        public override bool HitTest(Point p, double tolerance)
        {
            var hit = Geom.Inflate(Area, tolerance);
            if (!hit.Contains(p)) return false;
            if (Kind != ShapeKind.Ellipse) return true;
            double rx = Area.Width / 2, ry = Area.Height / 2;
            if (rx < 1 || ry < 1) return true;
            double nx = (p.X - (Area.X + rx)) / rx, ny = (p.Y - (Area.Y + ry)) / ry;
            return nx * nx + ny * ny <= 1.15;
        }

        public override Annotation Clone() => new RectAnnotation
        {
            Color = Color, Thickness = Thickness, Kind = Kind, Area = Area, BlockSize = BlockSize, Patch = Patch
        };
    }

    /// <summary>Straight line or arrow.</summary>
    public sealed class LineAnnotation : Annotation
    {
        public Point Start;
        public Point End;
        public bool Arrow;

        public override Rect Bounds => Geom.Normalize(Start, End);

        public override void Render(DrawingContext dc, double pixelsPerDip)
        {
            double len = (End - Start).Length;
            if (len < 1) return;
            var pen = PenOf(Color, Thickness, true);
            Point end = End;

            if (Arrow)
            {
                double head = Math.Max(8, 4.0 * pen.Thickness + 6);
                if (len > head * 0.8)
                {
                    var dir = (End - Start);
                    dir.Normalize();
                    var tip = End;
                    var back = new Point(tip.X - dir.X * head, tip.Y - dir.Y * head);
                    var normal = new Vector(-dir.Y, dir.X);
                    double half = head * 0.42;
                    var p1 = new Point(back.X + normal.X * half, back.Y + normal.Y * half);
                    var p2 = new Point(back.X - normal.X * half, back.Y - normal.Y * half);
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(tip, true, true);
                        ctx.LineTo(p1, true, false);
                        ctx.LineTo(p2, true, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(BrushOf(Color), null, geo);
                    end = back;
                }
            }
            dc.DrawLine(pen, Start, end);
        }

        public override bool HitTest(Point p, double tolerance) =>
            Geom.DistanceToSegment(p, Start, End) <= Math.Max(tolerance, Thickness / 2 + 3);

        public override Annotation Clone() => new LineAnnotation
        {
            Color = Color, Thickness = Thickness, Start = Start, End = End, Arrow = Arrow
        };
    }

    /// <summary>Freehand pen or highlighter stroke.</summary>
    public sealed class PenAnnotation : Annotation
    {
        public List<Point> Points = new List<Point>();
        public bool Highlighter;

        public override Rect Bounds
        {
            get
            {
                if (Points.Count == 0) return Rect.Empty;
                double minX = Points[0].X, minY = Points[0].Y, maxX = minX, maxY = minY;
                foreach (var p in Points)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }
                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
        }

        public override void Render(DrawingContext dc, double pixelsPerDip)
        {
            if (Points.Count < 2) return;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Points[0], false, false);
                bool first = true;
                foreach (var p in Points)
                {
                    if (first) { first = false; continue; }
                    ctx.LineTo(p, true, true);
                }
            }
            geo.Freeze();

            if (Highlighter)
            {
                var c = Color;
                var pen = new Pen(BrushOf(Color.FromArgb(70, c.R, c.G, c.B)), Math.Max(6, Thickness * 4))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
            else
            {
                dc.DrawGeometry(null, PenOf(Color, Thickness, true), geo);
            }
        }

        public override bool HitTest(Point p, double tolerance)
        {
            double tol = Math.Max(tolerance, (Highlighter ? Math.Max(6, Thickness * 4) : Thickness) / 2 + 3);
            for (int i = 1; i < Points.Count; i++)
            {
                if (Geom.DistanceToSegment(p, Points[i - 1], Points[i]) <= tol) return true;
            }
            return Points.Count == 1 && (p - Points[0]).Length <= tol;
        }

        public override Annotation Clone() => new PenAnnotation
        {
            Color = Color, Thickness = Thickness, Highlighter = Highlighter, Points = new List<Point>(Points)
        };
    }

    /// <summary>Text label.</summary>
    public sealed class TextAnnotation : Annotation
    {
        public Point Origin;
        public string Text = "";
        public double FontSize = 18;
        private Size _measured;

        public Size Measured => _measured;

        public double EffectiveFontSize => Math.Max(11, FontSize);

        private FormattedText Build(double pixelsPerDip)
        {
            var ft = new FormattedText(Text ?? "",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Fonts.Ui,
                EffectiveFontSize,
                Brushes.White,
                pixelsPerDip <= 0 ? 1.0 : pixelsPerDip);
            return ft;
        }

        public override Rect Bounds
        {
            get
            {
                if (_measured.Width <= 0 && !string.IsNullOrEmpty(Text))
                {
                    var ft = Build(1.0);
                    _measured = new Size(ft.Width, ft.Height);
                }
                return new Rect(Origin, new Size(Math.Max(4, _measured.Width), Math.Max(4, _measured.Height)));
            }
        }

        public override void Render(DrawingContext dc, double pixelsPerDip)
        {
            if (string.IsNullOrEmpty(Text)) return;
            var ft = Build(pixelsPerDip);
            _measured = new Size(ft.Width, ft.Height);

            var shadow = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Fonts.Ui, EffectiveFontSize, new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), pixelsPerDip <= 0 ? 1.0 : pixelsPerDip);
            dc.DrawText(shadow, new Point(Origin.X + 1.5, Origin.Y + 1.5));
            ft.SetForegroundBrush(BrushOf(Color));
            dc.DrawText(ft, Origin);
        }

        public override bool HitTest(Point p, double tolerance) => Geom.Inflate(Bounds, tolerance).Contains(p);

        public override Annotation Clone() => new TextAnnotation
        {
            Color = Color, Thickness = Thickness, Origin = Origin, Text = Text, FontSize = FontSize
        };
    }
}