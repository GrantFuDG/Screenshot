using System;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotManager.Interop;
using Gdi = System.Drawing;
using GdiImaging = System.Drawing.Imaging;

namespace ScreenshotManager.Services
{
    /// <summary>
    /// A frozen bitmap of the whole virtual screen (physical pixels, 1 DIP == 1 pixel) plus the raw
    /// pixel buffer so the magnifier / colour picker can read pixels without re-capturing.
    /// </summary>
    public sealed class ScreenSnapshot
    {
        public BitmapSource Image { get; init; }
        public Gdi.Rectangle Bounds { get; init; }
        public byte[] Pixels { get; init; }
        public int Stride { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }

        public System.Windows.Media.Color GetPixel(int screenX, int screenY)
        {
            int x = screenX - Bounds.X;
            int y = screenY - Bounds.Y;
            if (Pixels == null || x < 0 || y < 0 || x >= Width || y >= Height) return Colors.Black;
            int i = y * Stride + x * 4;
            return System.Windows.Media.Color.FromRgb(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
        }
    }

    /// <summary>GDI desktop capture helpers.</summary>
    public static class ScreenCaptureService
    {
        /// <summary>Captures every monitor into one image. Throws on failure.</summary>
        public static ScreenSnapshot CaptureVirtualScreen()
        {
            var bounds = Native.GetVirtualScreenBounds();
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                var primary = MonitorInfo.Primary();
                bounds = primary != null ? primary.Bounds : new Gdi.Rectangle(0, 0, 1920, 1080);
            }
            return Capture(bounds);
        }

        /// <summary>Captures one rectangle of the desktop.</summary>
        public static ScreenSnapshot Capture(Gdi.Rectangle bounds)
        {
            if (bounds.Width < 1 || bounds.Height < 1)
                throw new ArgumentException("capture bounds must be positive");

            Gdi.Bitmap bmp = null;
            try
            {
                bmp = new Gdi.Bitmap(bounds.Width, bounds.Height, GdiImaging.PixelFormat.Format32bppRgb);
                using (var g = Gdi.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Gdi.Size(bounds.Width, bounds.Height),
                        Gdi.CopyPixelOperation.SourceCopy);
                }

                var lockRect = new Gdi.Rectangle(0, 0, bounds.Width, bounds.Height);
                var data = bmp.LockBits(lockRect, ImageLockMode.ReadOnly, GdiImaging.PixelFormat.Format32bppRgb);
                byte[] pixels;
                int stride;
                try
                {
                    stride = data.Stride;
                    pixels = new byte[stride * bounds.Height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }

                var src = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
                src.Freeze();
                return new ScreenSnapshot
                {
                    Image = src,
                    Bounds = bounds,
                    Pixels = pixels,
                    Stride = stride,
                    Width = bounds.Width,
                    Height = bounds.Height
                };
            }
            finally
            {
                bmp?.Dispose();
            }
        }
    }
}