using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenshotManager.Interop;
using Gdi = System.Drawing;

namespace ScreenshotManager.Services
{
    /// <summary>Clipboard image helpers with retry (the clipboard is frequently locked by other apps).</summary>
    public static class ClipboardService
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        // Keeps the PNG stream handed to the clipboard alive while it owns the data.
        private static MemoryStream _pngKeepAlive;
        private static readonly object Gate = new object();

        public static bool SetImage(BitmapSource image)
        {
            if (image == null) return false;
            lock (Gate)
            {
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    try
                    {
                        var data = new DataObject();
                        data.SetData(DataFormats.Bitmap, image);

                        var png = new MemoryStream();
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        encoder.Save(png);
                        png.Position = 0;
                        data.SetData("PNG", png);
                        _pngKeepAlive = png;

                        Clipboard.SetDataObject(data, true);
                        return true;
                    }
                    catch (COMException)
                    {
                        Thread.Sleep(40);      // CLIPBRD_E_CANT_OPEN -> another process owns it
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("写入剪贴板失败", ex);
                        return false;
                    }
                }
                AppLog.Warn("写入剪贴板失败：剪贴板被占用");
                return false;
            }
        }

        public static bool ContainsImage()
        {
            try
            {
                var obj = Clipboard.GetDataObject();
                return obj != null && (obj.GetDataPresent("PNG") || obj.GetDataPresent(DataFormats.Bitmap)
                                       || obj.GetDataPresent(DataFormats.Dib));
            }
            catch
            {
                return false;
            }
        }

        public static BitmapSource GetImage()
        {
            try
            {
                var obj = Clipboard.GetDataObject();
                if (obj == null) return null;

                if (obj.GetDataPresent("PNG"))
                {
                    var raw = obj.GetData("PNG");
                    var ms = raw as MemoryStream;
                    if (ms != null)
                    {
                        var fromStream = FromStream(ms);
                        if (fromStream != null) return fromStream;
                    }
                    var bytes = raw as byte[];
                    if (bytes != null && bytes.Length > 0)
                    {
                        using (var s = new MemoryStream(bytes))
                        {
                            var fromBytes = FromStream(s);
                            if (fromBytes != null) return fromBytes;
                        }
                    }
                }

                if (obj.GetDataPresent(DataFormats.Bitmap))
                {
                    var data = obj.GetData(DataFormats.Bitmap);
                    var bs = data as BitmapSource;
                    if (bs != null)
                    {
                        var copy = BitmapFrame.Create(new WriteableBitmap(bs));
                        copy.Freeze();
                        return copy;
                    }
                    var gdi = data as Gdi.Bitmap;
                    if (gdi != null) return FromGdiBitmap(gdi);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("读取剪贴板图片失败", ex);
            }
            return null;
        }

        private static BitmapSource FromStream(Stream stream)
        {
            try
            {
                stream.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                AppLog.Warn("PNG 剪贴板数据解析失败: " + ex.Message);
                return null;
            }
        }

        public static BitmapSource FromGdiBitmap(Gdi.Bitmap bitmap)
        {
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                hbmp = bitmap.GetHbitmap();
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            }
        }
    }
}