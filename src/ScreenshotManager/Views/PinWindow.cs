using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotManager.Interop;
using ScreenshotManager.Models;
using ScreenshotManager.Services;

// WinForms/Drawing implicit usings make these names ambiguous; pin them to WPF.
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursor = System.Windows.Input.Cursor;
using Image = System.Windows.Controls.Image;
using Path = System.Windows.Shapes.Path;

namespace ScreenshotManager.Views
{
    /// <summary>
    /// A pinned image floating on the desktop.
    /// All geometry is kept in physical pixels and applied through SetWindowPos so that
    /// mixed-DPI monitors stay pixel-accurate; the visual tree is counter-scaled by the
    /// current monitor DPI so one content unit always equals one physical pixel.
    /// </summary>
    public sealed class PinWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly BitmapSource _image;
        private readonly Grid _root;
        private readonly Border _frame;
        private readonly Image _view;

        private Rect _phys;          // canonical window rectangle, physical pixels
        private double _scale = 1.0; // monitor DPI scale
        private double _zoom = 1.0;
        private bool _resizing;
        private int _zone;           // 0 none, else bitmask 1=left 2=right 4=top 8=bottom
        private Point _dragStart;    // content (physical) coords
        private Rect _dragRect;

        private const int MinSize = 24;
        private const int MaxSize = 12000;

        public BitmapSource Image => _image;

        public PinWindow(BitmapSource image, Rect physicalBounds, AppSettings settings)
        {
            _image = image;
            _settings = settings ?? new AppSettings();
            _phys = physicalBounds;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            AllowsTransparency = false;
            Topmost = _settings.PinTopmost;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "截图贴图";
            SnapsToDevicePixels = true;

            _view = new Image
            {
                Source = _image,
                Stretch = Stretch.Fill,
                Width = _image.PixelWidth,
                Height = _image.PixelHeight,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);

            _frame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x20, 0x20, 0x20)),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true,
                Child = _view
            };

            _root = new Grid { SnapsToDevicePixels = true };
            _root.Children.Add(_frame);
            Content = _root;

            ContextMenu = BuildMenu();
            Opacity = Math.Max(0.15, Math.Min(1.0, _settings.PinDefaultOpacity));

            UpdateMonitorScale();
            LayoutTransform = null;

            Loaded += OnLoaded;
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
            MouseWheel += OnWheel;
            MouseDoubleClick += (s, e) => { if (e.ChangedButton == MouseButton.Left) Close(); };
            PreviewKeyDown += OnKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && _settings.HidePinsFromCapture)
            {
                if (!Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE))
                {
                    Native.SetWindowDisplayAffinity(hwnd, 0x1); // WDA_MONITOR fallback
                }
            }
            ApplyBounds();
        }

        private void UpdateMonitorScale()
        {
            var monitor = MonitorInfo.FromPoint(new System.Drawing.Point(
                (int)Math.Round(_phys.Left + _phys.Width / 2), (int)Math.Round(_phys.Top + _phys.Height / 2)));
            double scale = monitor?.Scale ?? 1.0;
            if (Math.Abs(scale - _scale) < 0.001) return;
            _scale = scale;
            _root.LayoutTransform = new ScaleTransform(1.0 / _scale, 1.0 / _scale);
        }

        private void ApplyBounds()
        {
            _phys.Width = Clamp(_phys.Width, MinSize, MaxSize);
            _phys.Height = Clamp(_phys.Height, MinSize, MaxSize);
            _zoom = _phys.Width / _image.PixelWidth;
            _view.LayoutTransform = new ScaleTransform(_zoom, _zoom);
            _view.Width = _image.PixelWidth;
            _view.Height = _image.PixelHeight;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(_phys.X), (int)Math.Round(_phys.Y),
                (int)Math.Round(_phys.Width), (int)Math.Round(_phys.Height),
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        private static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            UpdateMonitorScale();
            ApplyBounds();
        }

        private int HitZone(Point p)
        {
            double edge = Math.Max(4, 6 / Math.Max(0.2, _zoom));
            int zone = 0;
            if (p.X <= edge) zone |= 1;
            else if (p.X >= _phys.Width - edge) zone |= 2;
            if (p.Y <= edge) zone |= 4;
            else if (p.Y >= _phys.Height - edge) zone |= 8;
            return zone;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var p = e.GetPosition(_root);
            _zone = HitZone(p);
            _dragStart = p;
            _dragRect = _phys;
            _resizing = _zone != 0;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_root);
            if (!_resizing && e.LeftButton != MouseButtonState.Pressed)
            {
                Cursor = CursorForZone(HitZone(p));
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed) return;

            double dx = p.X - _dragStart.X;
            double dy = p.Y - _dragStart.Y;

            if (_zone == 0)
            {
                _phys = new Rect(_dragRect.X + dx, _dragRect.Y + dy, _dragRect.Width, _dragRect.Height);
            }
            else
            {
                double aspect = _image.PixelWidth / (double)_image.PixelHeight;
                double w = _dragRect.Width, h = _dragRect.Height, x = _dragRect.X, y = _dragRect.Y;
                bool left = (_zone & 1) != 0, right = (_zone & 2) != 0;
                bool top = (_zone & 4) != 0, bottom = (_zone & 8) != 0;
                bool horizontal = left || right, vertical = top || bottom;

                bool useWidth = horizontal;
                if (horizontal && vertical) useWidth = Math.Abs(dx) >= Math.Abs(dy);

                if (useWidth)
                {
                    w = Clamp(_dragRect.Width + (left ? -dx : dx), MinSize, MaxSize);
                    h = w / aspect;
                }
                else
                {
                    h = Clamp(_dragRect.Height + (top ? -dy : dy), MinSize, MaxSize);
                    w = h * aspect;
                }

                if (left) x = _dragRect.Right - w;
                if (top) y = _dragRect.Bottom - h;
                _phys = new Rect(x, y, w, h);
            }

            UpdateMonitorScale();
            ApplyBounds();
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (IsMouseCaptured) ReleaseMouseCapture();
            _resizing = false;
            _zone = 0;
        }

        private static Cursor CursorForZone(int zone)
        {
            switch (zone)
            {
                case 1:
                case 2: return Cursors.SizeWE;
                case 4:
                case 8: return Cursors.SizeNS;
                case 5:
                case 10: return Cursors.SizeNWSE;
                case 6:
                case 9: return Cursors.SizeNESW;
                default: return Cursors.SizeAll;
            }
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                double step = e.Delta > 0 ? 0.05 : -0.05;
                Opacity = Clamp(Opacity + step, 0.15, 1.0);
                e.Handled = true;
                return;
            }

            var p = e.GetPosition(_root);
            double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            double newZoom = Clamp(_zoom * factor, 0.05, 20.0);
            double newW = _image.PixelWidth * newZoom;
            double newH = _image.PixelHeight * newZoom;
            double fx = _phys.Width <= 0 ? 0.5 : p.X / _phys.Width;
            double fy = _phys.Height <= 0 ? 0.5 : p.Y / _phys.Height;
            double gx = _phys.X + p.X;
            double gy = _phys.Y + p.Y;

            _phys = new Rect(gx - fx * newW, gy - fy * newH, newW, newH);
            UpdateMonitorScale();
            ApplyBounds();
            e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            switch (e.Key)
            {
                case Key.Escape:
                case Key.Delete:
                    Close();
                    e.Handled = true;
                    return;
                case Key.Left: _phys = new Rect(_phys.X - step, _phys.Y, _phys.Width, _phys.Height); ApplyBounds(); e.Handled = true; return;
                case Key.Right: _phys = new Rect(_phys.X + step, _phys.Y, _phys.Width, _phys.Height); ApplyBounds(); e.Handled = true; return;
                case Key.Up: _phys = new Rect(_phys.X, _phys.Y - step, _phys.Width, _phys.Height); ApplyBounds(); e.Handled = true; return;
                case Key.Down: _phys = new Rect(_phys.X, _phys.Y + step, _phys.Width, _phys.Height); ApplyBounds(); e.Handled = true; return;
                case Key.C:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        ClipboardService.SetImage(_image);
                        e.Handled = true;
                    }
                    return;
                case Key.S:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        SaveAs();
                        e.Handled = true;
                    }
                    return;
            }
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();

            var copy = new MenuItem { Header = "复制到剪贴板" };
            copy.Click += (s, e) => ClipboardService.SetImage(_image);

            var save = new MenuItem { Header = "另存为…" };
            save.Click += (s, e) => SaveAs();

            var resetZoom = new MenuItem { Header = "缩放 100%" };
            resetZoom.Click += (s, e) =>
            {
                double w = _image.PixelWidth, h = _image.PixelHeight;
                _phys = new Rect(_phys.X, _phys.Y, w, h);
                UpdateMonitorScale();
                ApplyBounds();
            };

            var opaque = new MenuItem { Header = "不透明度 100%" };
            opaque.Click += (s, e) => Opacity = 1.0;

            var topmost = new MenuItem { Header = "总在最前", IsCheckable = true, IsChecked = Topmost };
            topmost.Click += (s, e) => Topmost = topmost.IsChecked;

            var hideFromCapture = new MenuItem { Header = "截图时隐藏", IsCheckable = true, IsChecked = _settings.HidePinsFromCapture };
            hideFromCapture.Click += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                if (hideFromCapture.IsChecked) Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
                else Native.SetWindowDisplayAffinity(hwnd, 0);
            };

            var close = new MenuItem { Header = "关闭贴图" };
            close.Click += (s, e) => Close();

            menu.Items.Add(copy);
            menu.Items.Add(save);
            menu.Items.Add(new Separator());
            menu.Items.Add(resetZoom);
            menu.Items.Add(opaque);
            menu.Items.Add(topmost);
            menu.Items.Add(hideFromCapture);
            menu.Items.Add(new Separator());
            menu.Items.Add(close);
            return menu;
        }

        private void SaveAs()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存贴图",
                FileName = FileNames.BuildName(_settings, "Pin"),
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
                InitialDirectory = SafeInitialDirectory()
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                string ext = System.IO.Path.GetExtension(dialog.FileName).TrimStart('.').ToLowerInvariant();
                var encoder = ImageComposer.CreateEncoder(ext, _settings.JpegQuality);
                var frameSource = (ext == "jpg" || ext == "jpeg") ? ImageComposer.Flatten(_image) : _image;
                encoder.Frames.Add(BitmapFrame.Create(frameSource));
                using (var stream = System.IO.File.Create(dialog.FileName)) encoder.Save(stream);
                AppLog.Info("贴图已保存: " + dialog.FileName);
            }
            catch (Exception ex)
            {
                AppLog.Error("保存贴图失败", ex);
                MessageBox.Show(this, "保存失败：" + ex.Message, "截图管家", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string SafeInitialDirectory()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.SaveDirectory) && System.IO.Directory.Exists(_settings.SaveDirectory))
                    return _settings.SaveDirectory;
            }
            catch
            {
            }
            return AppPaths.DefaultSaveDir;
        }
    }
}