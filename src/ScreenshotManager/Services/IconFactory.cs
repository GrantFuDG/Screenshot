using System;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gdi = System.Drawing;

namespace ScreenshotManager.Services
{
    /// <summary>Draws the application / tray icon at runtime, so the project needs no binary assets.</summary>
    public static class IconFactory
    {
        public static Gdi.Icon CreateTrayIcon(int size = 32)
        {
            using (var bmp = new Gdi.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Gdi.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Gdi.Color.Transparent);
                    DrawGdi(g, size);
                }
                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (var tmp = Gdi.Icon.FromHandle(handle))
                    {
                        return (Gdi.Icon)tmp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static void DrawGdi(Gdi.Graphics g, int size)
        {
            float pad = size * 0.06f;
            var rect = new Gdi.RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
            using (var path = RoundedRect(rect, size * 0.22f))
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Gdi.PointF(rect.Left, rect.Top), new Gdi.PointF(rect.Right, rect.Bottom),
                Gdi.Color.FromArgb(255, 39, 116, 214), Gdi.Color.FromArgb(255, 22, 176, 168)))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Gdi.Pen(Gdi.Color.White, Math.Max(1.6f, size * 0.085f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float m = size * 0.24f;
                float far = size - m;
                float arm = size * 0.16f;
                // crop mark corners
                g.DrawLine(pen, m, m + arm, m, m);
                g.DrawLine(pen, m, m, m + arm, m);
                g.DrawLine(pen, far - arm, m, far, m);
                g.DrawLine(pen, far, m, far, m + arm);
                g.DrawLine(pen, far, far - arm, far, far);
                g.DrawLine(pen, far, far, far - arm, far);
                g.DrawLine(pen, m + arm, far, m, far);
                g.DrawLine(pen, m, far, m, far - arm);
            }

            using (var inner = new Gdi.SolidBrush(Gdi.Color.FromArgb(210, 255, 255, 255)))
            {
                float w = size * 0.26f, h = size * 0.18f;
                g.FillRectangle(inner, (size - w) / 2f, (size - h) / 2f, w, h);
            }
        }

        private static Gdi.Drawing2D.GraphicsPath RoundedRect(Gdi.RectangleF r, float radius)
        {
            var path = new Gdi.Drawing2D.GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>WPF icon for window chrome / dialogs.</summary>
        public static ImageSource CreateImageSource(int size = 64)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double pad = size * 0.06;
                var rect = new Rect(pad, pad, size - 2 * pad, size - 2 * pad);
                var brush = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(39, 116, 214),
                    System.Windows.Media.Color.FromRgb(22, 176, 168), 45);
                dc.DrawRoundedRectangle(brush, null, rect, size * 0.22, size * 0.22);

                var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, Math.Max(1.6, size * 0.085))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                double m = size * 0.24, far = size - m, arm = size * 0.16;
                dc.DrawLine(pen, new Point(m, m + arm), new Point(m, m));
                dc.DrawLine(pen, new Point(m, m), new Point(m + arm, m));
                dc.DrawLine(pen, new Point(far - arm, m), new Point(far, m));
                dc.DrawLine(pen, new Point(far, m), new Point(far, m + arm));
                dc.DrawLine(pen, new Point(far, far - arm), new Point(far, far));
                dc.DrawLine(pen, new Point(far, far), new Point(far - arm, far));
                dc.DrawLine(pen, new Point(m + arm, far), new Point(m, far));
                dc.DrawLine(pen, new Point(m, far), new Point(m, far - arm));

                var inner = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 255, 255, 255));
                inner.Freeze();
                dc.DrawRectangle(inner, null, new Rect((size - size * 0.26) / 2, (size - size * 0.18) / 2, size * 0.26, size * 0.18));
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}