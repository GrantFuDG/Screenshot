using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotManager.Models;
using Gdi = System.Drawing;

namespace ScreenshotManager.Services
{
    /// <summary>Crop / mosaic / composition / encode helpers. All coordinates are global physical pixels.</summary>
    public static class ImageComposer
    {
        public static BitmapSource Crop(BitmapSource source, Gdi.Rectangle sourceBounds, Rect rect)
        {
            if (source == null) return null;
            var clamped = ClampToSource(rect, sourceBounds);
            if (clamped.Width < 1 || clamped.Height < 1) return null;
            var cropped = new CroppedBitmap(source, new Int32Rect(
                (int)Math.Round(clamped.X - sourceBounds.X),
                (int)Math.Round(clamped.Y - sourceBounds.Y),
                (int)Math.Round(clamped.Width),
                (int)Math.Round(clamped.Height)));
            cropped.Freeze();
            return cropped;
        }

        public static Rect ClampToSource(Rect rect, Gdi.Rectangle sourceBounds)
        {
            var b = Geom.ToRect(sourceBounds);
            var r = Rect.Intersect(rect, b);
            if (r.IsEmpty) return Rect.Empty;
            return r;
        }

        /// <summary>
        /// Builds the mosaic (pixelate) or blur patch for a region. Rendered once, outside the WPF render pass,
        /// so the overlay can just blit it 1:1.
        /// </summary>
        public static BitmapSource BuildPatch(BitmapSource source, Gdi.Rectangle sourceBounds, Rect rect, int block, bool blur)
        {
            var area = ClampToSource(rect, sourceBounds);
            if (area.Width < 1 || area.Height < 1) return null;
            block = Math.Max(2, block);

            var crop = Crop(source, sourceBounds, area);
            if (crop == null) return null;

            int w = Math.Max(1, (int)Math.Round(area.Width));
            int h = Math.Max(1, (int)Math.Round(area.Height));
            int bw = Math.Max(1, (int)Math.Round(w / (double)block));
            int bh = Math.Max(1, (int)Math.Round(h / (double)block));

            // 1) box-downscale to one pixel per block (HighQuality -> proper averaging)
            var down = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(down, BitmapScalingMode.HighQuality);
            using (var dc = down.RenderOpen())
            {
                dc.DrawImage(crop, new Rect(0, 0, bw, bh));
            }
            var small = new RenderTargetBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32);
            small.Render(down);
            small.Freeze();

            // 2) scale back up: nearest neighbour = mosaic, smooth = blur
            var up = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(up, blur ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
            using (var dc = up.RenderOpen())
            {
                dc.DrawImage(small, new Rect(0, 0, w, h));
            }
            var full = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            full.Render(up);
            full.Freeze();
            return full;
        }

        /// <summary>Renders the selected region plus every annotation into a new bitmap of exactly the selection size.</summary>
        public static BitmapSource Compose(ScreenSnapshot snapshot, Rect selection, IList<Annotation> annotations)
        {
            var sel = ClampToSource(selection, snapshot.Bounds);
            if (sel.Width < 1 || sel.Height < 1) return null;
            int w = Math.Max(1, (int)Math.Round(sel.Width));
            int h = Math.Max(1, (int)Math.Round(sel.Height));

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-sel.X, -sel.Y));
                dc.PushClip(new RectangleGeometry(sel));
                dc.DrawRectangle(Brushes.Black, null, sel);   // opaque backdrop (JPEG / transparency safety)
                dc.DrawImage(snapshot.Image, Geom.ToRect(snapshot.Bounds));
                if (annotations != null)
                {
                    foreach (var a in annotations) a.Render(dc, 1.0);
                }
                dc.Pop();
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        public static BitmapSource Flatten(BitmapSource image)
        {
            if (image == null) return null;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            }
            var rtb = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        public static BitmapEncoder CreateEncoder(string format, int jpegQuality)
        {
            switch ((format ?? "png").ToLowerInvariant())
            {
                case "jpg":
                case "jpeg":
                    return new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 10, 100) };
                case "bmp":
                    return new BmpBitmapEncoder();
                default:
                    return new PngBitmapEncoder();
            }
        }

        public static string NormalizeExtension(string format)
        {
            switch ((format ?? "png").ToLowerInvariant())
            {
                case "jpg":
                case "jpeg": return ".jpg";
                case "bmp": return ".bmp";
                default: return ".png";
            }
        }

        /// <summary>Writes the image to disk. Returns the path actually written.</summary>
        public static string Save(BitmapSource image, string directory, string fileNameWithoutExtension, string format, int jpegQuality)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            string ext = NormalizeExtension(format);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileNameWithoutExtension + ext);
            path = MakeUnique(path);
            var encoder = CreateEncoder(format, jpegQuality);
            BitmapSource toEncode = image;
            if (ext == ".jpg") toEncode = Flatten(image);
            encoder.Frames.Add(BitmapFrame.Create(toEncode));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(fs);
            }
            return path;
        }

        public static string MakeUnique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 10000; i++)
            {
                string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
        }

        public static BitmapSource LoadFromFile(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}