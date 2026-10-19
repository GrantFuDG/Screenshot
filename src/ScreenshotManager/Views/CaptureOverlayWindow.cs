using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotManager.Interop;
using ScreenshotManager.Models;
using ScreenshotManager.Services;
using Gdi = System.Drawing;

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
using Fonts = ScreenshotManager.Models.Fonts;

namespace ScreenshotManager.Views
{
    /// <summary>
    /// One full-monitor overlay window. All of them share a single <see cref="CaptureSession"/>,
    /// so a selection or annotation can be dragged across monitor borders.
    /// </summary>
    public sealed class CaptureOverlayWindow : Window
    {
        private readonly CaptureSession _session;
        private readonly MonitorInfo _monitor;
        private readonly Canvas _canvas;
        private readonly OverlaySurface _surface;
        private readonly AnnotationToolbar _toolbar;

        private double _uiScale = 1.0;
        private bool _leftDown;
        private bool _selecting;
        private bool _erasing;
        private int _dragZone;
        private IntPtr _lastHoverHwnd = IntPtr.Zero;
        private Rect _dragStart = Rect.Empty;
        private Annotation _current;
        private Point _annotationOrigin;
        private double _magnifierZoom = 4;
        private bool _positioning;

        /// <summary>诊断开关（环境变量 SM_DEBUG_CAPTURE=1）：把鼠标映射与选区数值写进日志。</summary>
        private static readonly bool VerboseCapture =
            Environment.GetEnvironmentVariable("SM_DEBUG_CAPTURE") == "1";

        private static void Diag(string message)
        {
            if (VerboseCapture) AppLog.Info("[捕获诊断] " + message);
        }

        private static string Fmt(double value) => value.ToString("0.##");

        private static string FmtRect(Rect r) =>
            r.IsEmpty ? "(空)" : Fmt(r.X) + "," + Fmt(r.Y) + " " + Fmt(r.Width) + "x" + Fmt(r.Height);

        /// <summary>把「表面局部坐标 / 全局坐标 / 真实光标 / 窗口实际位置」一起记下来，用来定位映射错误。</summary>
        private string DescribeMapping(Point local, Point g)
        {
            string cursor = "(取不到)";
            if (Native.TryGetCursorPos(out var cp)) cursor = cp.X + "," + cp.Y;
            return "local=" + Fmt(local.X) + "," + Fmt(local.Y)
                + " g=" + Fmt(g.X) + "," + Fmt(g.Y)
                + " 真实光标=" + cursor
                + " 窗口Left/Top=" + Fmt(Left) + "," + Fmt(Top)
                + " 窗口Size=" + Fmt(ActualWidth) + "x" + Fmt(ActualHeight)
                + " 显示器=" + _monitor.Bounds.X + "," + _monitor.Bounds.Y + " "
                + _monitor.Bounds.Width + "x" + _monitor.Bounds.Height
                + " 显示器Scale=" + _monitor.Scale.ToString("0.###")
                + " uiScale=" + _uiScale.ToString("0.###");
        }

        public MonitorInfo Monitor => _monitor;

        public CaptureOverlayWindow(CaptureSession session, MonitorInfo monitor)
        {
            _session = session;
            _monitor = monitor;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = false;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Focusable = true;
            ShowActivated = true;
            Title = "截图";
            Icon = IconFactory.CreateImageSource(32);

            _surface = new OverlaySurface(_session, _monitor)
            {
                Width = Math.Max(1, _monitor.Width),
                Height = Math.Max(1, _monitor.Height),
                ClipToBounds = true,
                Focusable = true,
                Cursor = Cursors.Cross
            };
            _surface.MouseMove += OnSurfaceMouseMove;
            _surface.MouseLeftButtonDown += OnSurfaceMouseLeftDown;
            _surface.MouseLeftButtonUp += OnSurfaceMouseLeftUp;
            _surface.MouseRightButtonDown += OnSurfaceMouseRightDown;
            _surface.MouseWheel += OnSurfaceMouseWheel;
            _surface.MouseEnter += (s, e) => _surface.Focus();

            _canvas = new Canvas();
            _canvas.Children.Add(_surface);

            _toolbar = new AnnotationToolbar(_session.Settings) { Visibility = Visibility.Collapsed };
            Panel.SetZIndex(_toolbar, 100);
            _canvas.Children.Add(_toolbar);
            HookToolbar();
            MakeUnfocusable(_toolbar);

            Content = _canvas;

            double primaryScale = MonitorInfo.Primary()?.Scale ?? 1.0;
            if (primaryScale <= 0.1) primaryScale = 1.0;
            Left = _monitor.Bounds.X / primaryScale;
            Top = _monitor.Bounds.Y / primaryScale;
            Width = Math.Max(1, _monitor.Width / Math.Max(0.1, _monitor.Scale));
            Height = Math.Max(1, _monitor.Height / Math.Max(0.1, _monitor.Scale));

            _magnifierZoom = Math.Max(1.5, Math.Min(12, _session.Settings.MagnifierZoom));
            _session.Changed += OnSessionChanged;
            _toolbar.LayoutUpdated += (s, e) => PositionToolbar();

            Loaded += (s, e) =>
            {
                UpdateScale();
                Activate();
                _surface.Focus();
                _surface.InvalidateVisual();
            };
            Closed += (s, e) => _session.Changed -= OnSessionChanged;
        }

        // ------------------------------------------------------------------ window plumbing

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            PlaceWindow(hwnd);
            UpdateScale();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) PlaceWindow(hwnd);
            UpdateScale();
        }

        private void PlaceWindow(IntPtr hwnd)
        {
            try
            {
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST,
                    _monitor.Bounds.X, _monitor.Bounds.Y, _monitor.Bounds.Width, _monitor.Bounds.Height,
                    Native.SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                AppLog.Warn("遮罩窗口定位失败", ex);
            }
        }

        /// <summary>Re-reads the monitor DPI and makes 1 layout unit == 1 physical pixel.</summary>
        private void UpdateScale()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            double scale = hwnd != IntPtr.Zero ? Native.GetDpiForWindow(hwnd) / 96.0 : _monitor.Scale;
            if (scale <= 0.1) scale = 1.0;
            _uiScale = scale;

            _canvas.LayoutTransform = new ScaleTransform(1.0 / _uiScale, 1.0 / _uiScale);
            _surface.UiScale = _uiScale;
            _surface.MagnifierZoom = _magnifierZoom;
            _surface.Width = Math.Max(1, _monitor.Width);
            _surface.Height = Math.Max(1, _monitor.Height);
            _toolbar.SetUiScale(_uiScale);

            Width = Math.Max(1, _monitor.Width / _uiScale);
            Height = Math.Max(1, _monitor.Height / _uiScale);
            UpdateLayout();
            PositionToolbar();
            _surface.InvalidateVisual();
        }

        private static void MakeUnfocusable(DependencyObject root)
        {
            if (root == null) return;
            if (root is Control c) c.Focusable = false;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) MakeUnfocusable(VisualTreeHelper.GetChild(root, i));
        }

        // ------------------------------------------------------------------ session / toolbar sync

        private void OnSessionChanged()
        {
            _surface.InvalidateVisual();
            SyncToolbar();
        }

        private void SyncToolbar()
        {
            bool host = IsToolbarHost();
            bool show = host && _session.HasSelection;
            _toolbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _toolbar.SetActiveTool(_session.Tool);
            _toolbar.SetColor(_session.Color);
            _toolbar.SetThickness(_session.Thickness);
            _toolbar.SetUndoRedo(_session.CanUndo, _session.CanRedo);
            if (show) PositionToolbar();
        }

        private bool IsToolbarHost()
        {
            var target = _session.ToolbarMonitor();
            return target != null && target.Handle == _monitor.Handle;
        }

        private void PositionToolbar()
        {
            if (_positioning) return;
            if (_toolbar.Visibility != Visibility.Visible) return;
            _positioning = true;
            try
            {
                double S = _uiScale;
                double w = _toolbar.ScaledWidth;
                double h = _toolbar.ActualHeight > 1 ? _toolbar.ActualHeight * S : 46 * S;
                var sel = _session.ClampedSelection();
                var mon = Geom.ToRect(_monitor.Bounds);

                double x = sel.Left;
                double y = sel.Bottom + 10 * S;
                if (y + h > mon.Bottom) y = sel.Top - h - 10 * S;
                if (x + w > mon.Right - 4) x = mon.Right - w - 4;
                if (x < mon.Left + 4) x = mon.Left + 4;
                if (y < mon.Top + 4) y = mon.Top + 4;
                if (y + h > mon.Bottom) y = Math.Max(mon.Top + 4, mon.Bottom - h - 4);

                Canvas.SetLeft(_toolbar, x - mon.Left);
                Canvas.SetTop(_toolbar, y - mon.Top);
            }
            finally
            {
                _positioning = false;
            }
        }

        private void HookToolbar()
        {
            _toolbar.ToolSelected += kind =>
            {
                _session.Tool = kind;
                _session.MosaicBlurMode = kind == ToolKind.Blur;
                _surface.Focus();
                _session.RaiseChanged();
            };
            _toolbar.ColorChanged += c => { _session.Color = c; _surface.Focus(); };
            _toolbar.ThicknessChanged += t => { _session.Thickness = t; _surface.Focus(); };
            _toolbar.UndoRequested += () => { _session.Undo(); _surface.Focus(); };
            _toolbar.RedoRequested += () => { _session.Redo(); _surface.Focus(); };
            _toolbar.CopyRequested += () => _session.RequestCopy();
            _toolbar.SaveRequested += () => _session.RequestSave();
            _toolbar.PinRequested += () => _session.RequestPin();
            _toolbar.ConfirmRequested += () => _session.RequestConfirm();
            _toolbar.CancelRequested += () => _session.RequestCancel();
        }

        // ------------------------------------------------------------------ mouse

        private Point ToGlobal(Point local) => new Point(local.X + _monitor.Bounds.X, local.Y + _monitor.Bounds.Y);

        private Rect ClampToVirtual(Rect r)
        {
            var v = Geom.ToRect(_session.VirtualBounds);
            double x = Math.Max(v.Left, Math.Min(r.Left, v.Right));
            double y = Math.Max(v.Top, Math.Min(r.Top, v.Bottom));
            double w = Math.Min(r.Width, v.Right - x);
            double h = Math.Min(r.Height, v.Bottom - y);
            return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
        }

        private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
        {
            var local = e.GetPosition(_surface);
            var g = ToGlobal(local);
            _session.Cursor = local;
            _session.CursorGlobal = g;

            if (_selecting) UpdateSelectionDrag(g);
            else if (_current != null) UpdateAnnotationDrag(g);
            else if (_erasing && _leftDown) _session.EraseAt(g);
            else UpdateHover(g);

            _session.RaiseChanged();
        }

        private void OnSurfaceMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            _surface.Focus();
            var g = ToGlobal(e.GetPosition(_surface));
            _session.CursorGlobal = g;
            Diag("按下 " + DescribeMapping(e.GetPosition(_surface), g));

            if (_session.EditingText != null) CommitText();

            if (e.ClickCount == 2)
            {
                if (_session.HasSelection && _session.Selection.Contains(g)) _session.RequestConfirm();
                else BeginSelectionDrag(g);
                e.Handled = true;
                return;
            }

            _leftDown = true;
            if (!_session.HasSelection)
            {
                BeginSelectionDrag(g);
            }
            else
            {
                int zone = SelectionZone(g);
                if (zone != 0) BeginSelectionDrag(g, zone);
                else if (_session.Selection.Contains(g)) BeginAnnotation(g);
                else
                {
                    _session.Annotations.Clear();
                    BeginSelectionDrag(g);
                }
            }

            if (!_surface.IsMouseCaptured) _surface.CaptureMouse();
            e.Handled = true;
        }

        private void OnSurfaceMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            _leftDown = false;
            _erasing = false;
            if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();

            var g = ToGlobal(e.GetPosition(_surface));
            _session.CursorGlobal = g;
            Diag("抬起 " + DescribeMapping(e.GetPosition(_surface), g) + " 选择=" + FmtRect(_session.Selection));

            if (_selecting)
            {
                _selecting = false;
                var sel = ClampToVirtual(_session.Selection);
                if (sel.Width < 4 || sel.Height < 4)
                {
                    if (_session.HasHoverWindow && _session.HoverWindow.Width > 4 && _session.HoverWindow.Height > 4)
                    {
                        _session.Selection = ClampToVirtual(_session.HoverWindow);
                        _session.HasSelection = true;
                    }
                    else
                    {
                        _session.Selection = Rect.Empty;
                        _session.HasSelection = false;
                    }
                }
                else
                {
                    _session.Selection = sel;
                    _session.HasSelection = true;
                }
                _session.Phase = _session.HasSelection ? CapturePhase.Annotating : CapturePhase.Hovering;
                SyncToolbar();
                _session.RaiseChanged();
            }

            if (_current != null) FinishAnnotation();
            e.Handled = true;
        }

        private void OnSurfaceMouseRightDown(object sender, MouseButtonEventArgs e)
        {
            // 右键 = 一次退出整个截图（用户要求：不用去够 Esc）。
            // 想重新框选不必先清选区——在选区外左键拖拽会清空标注并重新开始，见 OnSurfaceMouseLeftDown。
            if (_session.EditingText != null) CancelText();
            _current = null;
            _session.InProgress = null;
            _session.RequestCancel();
            e.Handled = true;
        }

        private void OnSurfaceMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_current != null) return;
            double z = _magnifierZoom + (e.Delta > 0 ? 0.5 : -0.5);
            _magnifierZoom = Math.Max(1.5, Math.Min(12.0, z));
            _surface.MagnifierZoom = _magnifierZoom;
            _surface.InvalidateVisual();
            e.Handled = true;
        }

        // ------------------------------------------------------------------ selection drag

        private int SelectionZone(Point g)
        {
            if (!_session.HasSelection) return 0;
            double t = Math.Max(5, 8 * _uiScale);
            var r = _session.Selection;
            bool inX = g.X >= r.Left - t && g.X <= r.Right + t;
            bool inY = g.Y >= r.Top - t && g.Y <= r.Bottom + t;
            if (!inX || !inY) return 0;
            bool inner = g.X > r.Left + t && g.X < r.Right - t && g.Y > r.Top + t && g.Y < r.Bottom - t;
            if (inner) return 0;
            int zone = 0;
            if (Math.Abs(g.X - r.Left) <= t) zone |= 1;
            else if (Math.Abs(g.X - r.Right) <= t) zone |= 2;
            if (Math.Abs(g.Y - r.Top) <= t) zone |= 4;
            else if (Math.Abs(g.Y - r.Bottom) <= t) zone |= 8;
            return zone;
        }

        private void BeginSelectionDrag(Point g, int forcedZone = -1)
        {
            if (forcedZone > 0)
            {
                _dragStart = _session.Selection;
                _dragZone = forcedZone;
            }
            else
            {
                // 拖拽永远以「按下点」为起点。以前这里有一条「鼠标下有窗口吸附」的分支：
                // 一旦悬停检测把桌面当成窗口（它铺满整屏），按下点就会落到整屏矩形的角点上，
                // 选区于是从屏幕左上/右上角开始拉。窗口点选改由 MouseUp 的「位移极小」分支处理。
                // 区域位 10 = 右/下边跟随光标，左/上边固定在按下点（15 会让四条边都跟随光标，
                // 选区退化成零面积）。
                _dragStart = new Rect(g, g);
                _dragZone = 10;
                _session.Selection = _dragStart;
                _session.HasSelection = true;
            }

            Diag("开始拖选 zone=" + _dragZone + " dragStart=" + FmtRect(_dragStart)
                + " 选择=" + FmtRect(_session.Selection) + " 悬浮窗=" + FmtRect(_session.HoverWindow));
            _selecting = true;
            _erasing = false;
            _session.Phase = CapturePhase.Selecting;
            SyncToolbar();
            _session.RaiseChanged();
        }

        private void UpdateSelectionDrag(Point g)
        {
            double left = (_dragZone & 1) != 0 ? g.X : _dragStart.Left;
            double right = (_dragZone & 2) != 0 ? g.X : _dragStart.Right;
            double top = (_dragZone & 4) != 0 ? g.Y : _dragStart.Top;
            double bottom = (_dragZone & 8) != 0 ? g.Y : _dragStart.Bottom;

            var r = new Rect(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _dragZone == 10)
            {
                double side = Math.Max(r.Width, r.Height);
                double x = g.X >= _dragStart.Left ? _dragStart.Left : _dragStart.Left - side;
                double y = g.Y >= _dragStart.Top ? _dragStart.Top : _dragStart.Top - side;
                r = new Rect(x, y, side, side);
            }
            _session.Selection = ClampToVirtual(r);
            _session.RaiseChanged();
        }

        private void UpdateHover(Point g)
        {
            var s = _session.Settings;
            if (!s.DetectWindowOnHover || _session.HasSelection || _current != null)
            {
                if (_session.HasHoverWindow)
                {
                    _session.HasHoverWindow = false;
                    _session.RaiseChanged();
                }
                return;
            }

            try
            {
                var hwnd = Native.GetWindowUnderPoint(new Gdi.Point((int)Math.Round(g.X), (int)Math.Round(g.Y)), Native.CurrentProcessId);
                if (hwnd != IntPtr.Zero && Native.TryGetWindowBounds(hwnd, out var bounds) && bounds.Width > 2 && bounds.Height > 2)
                {
                    var rect = Geom.ToRect(bounds);
                    if (!_session.HasHoverWindow || _lastHoverHwnd != hwnd || _session.HoverWindow != rect)
                    {
                        Diag("悬停命中 hwnd=0x" + hwnd.ToInt64().ToString("X") + " class=" + Native.GetWindowClassName(hwnd)
                            + " bounds=" + bounds.X + "," + bounds.Y + " " + bounds.Width + "x" + bounds.Height
                            + " g=" + Fmt(g.X) + "," + Fmt(g.Y));
                    }
                    _lastHoverHwnd = hwnd;
                    _session.HoverWindow = rect;
                    _session.HasHoverWindow = true;
                }
                else
                {
                    if (_session.HasHoverWindow || _lastHoverHwnd != IntPtr.Zero)
                    {
                        Diag("悬停无窗口 hwnd=0x" + hwnd.ToInt64().ToString("X")
                            + " g=" + Fmt(g.X) + "," + Fmt(g.Y) + "（桌面/被过滤/0 尺寸）");
                    }
                    _lastHoverHwnd = IntPtr.Zero;
                    _session.HasHoverWindow = false;
                }
            }
            catch
            {
                _session.HasHoverWindow = false;
            }
        }

        // ------------------------------------------------------------------ annotation drag

        private void BeginAnnotation(Point g)
        {
            var sel = _session.Selection;
            if (!sel.Contains(g)) return;
            _annotationOrigin = g;
            _session.Phase = CapturePhase.Annotating;

            switch (_session.Tool)
            {
                case ToolKind.Rectangle:
                    _current = NewRect(ShapeKind.Rectangle, g);
                    break;
                case ToolKind.Ellipse:
                    _current = NewRect(ShapeKind.Ellipse, g);
                    break;
                case ToolKind.Mosaic:
                    _current = NewRect(ShapeKind.Mosaic, g);
                    break;
                case ToolKind.Blur:
                    _current = NewRect(ShapeKind.Blur, g);
                    break;
                case ToolKind.Line:
                    _current = new LineAnnotation { Start = g, End = g, Arrow = false, Color = _session.Color, Thickness = _session.Thickness };
                    break;
                case ToolKind.Arrow:
                    _current = new LineAnnotation { Start = g, End = g, Arrow = true, Color = _session.Color, Thickness = _session.Thickness };
                    break;
                case ToolKind.Pen:
                case ToolKind.Highlighter:
                {
                    var pen = new PenAnnotation { Highlighter = _session.Tool == ToolKind.Highlighter, Color = _session.Color, Thickness = _session.Thickness };
                    pen.Points.Add(g);
                    _current = pen;
                    break;
                }
                case ToolKind.Text:
                    BeginText(g);
                    return;
                case ToolKind.Eraser:
                    _erasing = true;
                    _session.EraseAt(g);
                    return;
                default:
                    return;
            }

            _session.InProgress = _current;
            _session.RaiseChanged();
        }

        private RectAnnotation NewRect(ShapeKind kind, Point g)
        {
            double thickness = kind == ShapeKind.Mosaic || kind == ShapeKind.Blur ? 1 : _session.Thickness;
            return new RectAnnotation
            {
                Kind = kind,
                Area = new Rect(g, g),
                Color = _session.Color,
                Thickness = thickness,
                BlockSize = Math.Max(3, _session.Settings.MosaicBlockSize)
            };
        }

        private void UpdateAnnotationDrag(Point g)
        {
            if (_current is RectAnnotation ra)
            {
                var r = Geom.Normalize(_annotationOrigin, g);
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && ra.Kind != ShapeKind.Mosaic && ra.Kind != ShapeKind.Blur)
                {
                    double side = Math.Max(r.Width, r.Height);
                    double x = g.X >= _annotationOrigin.X ? _annotationOrigin.X : _annotationOrigin.X - side;
                    double y = g.Y >= _annotationOrigin.Y ? _annotationOrigin.Y : _annotationOrigin.Y - side;
                    r = new Rect(x, y, side, side);
                }
                var sel = _session.Selection;
                r.Intersect(sel);
                ra.Area = r;
            }
            else if (_current is LineAnnotation la)
            {
                var end = g;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    var d = g - la.Start;
                    if (Math.Abs(d.X) > Math.Abs(d.Y)) end = new Point(g.X, la.Start.Y);
                    else end = new Point(la.Start.X, g.Y);
                }
                la.End = end;
            }
            else if (_current is PenAnnotation pa)
            {
                if (pa.Points.Count == 0 || (pa.Points[pa.Points.Count - 1] - g).Length > 1.5) pa.Points.Add(g);
            }
            _session.RaiseChanged();
        }

        private void FinishAnnotation()
        {
            var a = _current;
            _current = null;
            _session.InProgress = null;
            if (a == null) return;

            bool keep = true;
            if (a is RectAnnotation ra && (ra.Area.Width < 2 || ra.Area.Height < 2)) keep = false;
            if (a is LineAnnotation la && (la.End - la.Start).Length < 3) keep = false;
            if (a is PenAnnotation pa && pa.Points.Count < 2) keep = false;

            if (keep)
            {
                if (a is RectAnnotation patchTarget && (patchTarget.Kind == ShapeKind.Mosaic || patchTarget.Kind == ShapeKind.Blur))
                {
                    try
                    {
                        patchTarget.Patch = ImageComposer.BuildPatch(_session.Snapshot.Image, _session.VirtualBounds,
                            patchTarget.Area, Math.Max(3, patchTarget.BlockSize), patchTarget.Kind == ShapeKind.Blur);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("生成马赛克/模糊贴片失败", ex);
                    }
                }
                _session.AddAnnotation(a);
            }
            _session.RaiseChanged();
        }

        // ------------------------------------------------------------------ text

        private void BeginText(Point g)
        {
            var sel = _session.Selection;
            var origin = new Point(
                Math.Max(sel.Left + 2, Math.Min(g.X, sel.Right - 24)),
                Math.Max(sel.Top + 2, Math.Min(g.Y, sel.Bottom - 20)));
            var ta = new TextAnnotation
            {
                Origin = origin,
                Text = "",
                Color = _session.Color,
                FontSize = 18
            };
            _session.EditingText = ta;
            _session.InProgress = ta;
            _session.RaiseChanged();
        }

        private void CommitText()
        {
            var ta = _session.EditingText;
            _session.EditingText = null;
            _session.InProgress = null;
            if (ta != null && !string.IsNullOrWhiteSpace(ta.Text)) _session.AddAnnotation(ta);
            _session.RaiseChanged();
        }

        private void CancelText()
        {
            _session.EditingText = null;
            _session.InProgress = null;
            _session.RaiseChanged();
        }

        private void OnWindowTextInput(object sender, TextCompositionEventArgs e)
        {
            var ta = _session.EditingText;
            if (ta == null) return;
            ta.Text += e.Text;
            _session.RaiseChanged();
            e.Handled = true;
        }

        // ------------------------------------------------------------------ keyboard

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            var ta = _session.EditingText;
            if (ta != null)
            {
                switch (e.Key)
                {
                    case Key.Escape:
                        CancelText();
                        e.Handled = true;
                        return;
                    case Key.Enter:
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ta.Text += "\n";
                        else CommitText();
                        e.Handled = true;
                        return;
                    case Key.Back:
                        if (ta.Text.Length > 0) ta.Text = ta.Text.Substring(0, ta.Text.Length - 1);
                        _session.RaiseChanged();
                        e.Handled = true;
                        return;
                    case Key.Delete:
                        CancelText();
                        e.Handled = true;
                        return;
                }
                return;
            }

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.C: _session.RequestCopy(); e.Handled = true; return;
                    case Key.S: _session.RequestSave(); e.Handled = true; return;
                    case Key.T: _session.RequestPin(); e.Handled = true; return;
                    case Key.Z: _session.Undo(); e.Handled = true; return;
                    case Key.Y: _session.Redo(); e.Handled = true; return;
                    case Key.A:
                        _session.Selection = Geom.ToRect(_session.VirtualBounds);
                        _session.HasSelection = true;
                        _session.Phase = CapturePhase.Annotating;
                        SyncToolbar();
                        _session.RaiseChanged();
                        e.Handled = true;
                        return;
                }
                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    if (_current != null)
                    {
                        _current = null;
                        _session.InProgress = null;
                    }
                    else if (_session.HasSelection)
                    {
                        _session.Annotations.Clear();
                        _session.Selection = Rect.Empty;
                        _session.HasSelection = false;
                        _session.Phase = CapturePhase.Hovering;
                    }
                    else
                    {
                        _session.RequestCancel();
                        e.Handled = true;
                        return;
                    }
                    SyncToolbar();
                    _session.RaiseChanged();
                    e.Handled = true;
                    return;

                case Key.Enter:
                    if (_session.HasSelection) _session.RequestConfirm();
                    e.Handled = true;
                    return;

                case Key.Left:
                case Key.Right:
                case Key.Up:
                case Key.Down:
                    if (_session.HasSelection)
                    {
                        double step = shift ? 10 : 1;
                        var r = _session.Selection;
                        if (e.Key == Key.Left) r.Offset(-step, 0);
                        else if (e.Key == Key.Right) r.Offset(step, 0);
                        else if (e.Key == Key.Up) r.Offset(0, -step);
                        else r.Offset(0, step);
                        _session.Selection = ClampToVirtual(r);
                        _session.RaiseChanged();
                        e.Handled = true;
                    }
                    return;
            }

            ToolKind? tool = null;
            switch (e.Key)
            {
                case Key.R: tool = ToolKind.Rectangle; break;
                case Key.E: tool = ToolKind.Ellipse; break;
                case Key.L: tool = ToolKind.Line; break;
                case Key.A: tool = ToolKind.Arrow; break;
                case Key.P: tool = ToolKind.Pen; break;
                case Key.H: tool = ToolKind.Highlighter; break;
                case Key.T: tool = ToolKind.Text; break;
                case Key.M: tool = ToolKind.Mosaic; break;
                case Key.B: tool = ToolKind.Blur; break;
                case Key.X: tool = ToolKind.Eraser; break;
            }
            if (tool.HasValue && _session.HasSelection)
            {
                _session.Tool = tool.Value;
                _session.MosaicBlurMode = tool.Value == ToolKind.Blur;
                SyncToolbar();
                _session.RaiseChanged();
                e.Handled = true;
            }
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            base.OnPreviewTextInput(e);
            OnWindowTextInput(this, e);
        }

        // ================================================================== rendering

        private sealed class OverlaySurface : FrameworkElement
        {
            private readonly CaptureSession _session;
            private readonly MonitorInfo _monitor;

            public double UiScale { get; set; } = 1.0;
            public double MagnifierZoom { get; set; } = 4;

            public OverlaySurface(CaptureSession session, MonitorInfo monitor)
            {
                _session = session;
                _monitor = monitor;
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
                RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            }

            protected override void OnRender(DrawingContext dc)
            {
                double S = UiScale <= 0.1 ? 1.0 : UiScale;
                var mon = Geom.ToRect(_monitor.Bounds);
                var virtualRect = Geom.ToRect(_session.VirtualBounds);

                dc.PushTransform(new TranslateTransform(-mon.X, -mon.Y));
                dc.PushClip(new RectangleGeometry(mon));
                try
                {
                    // frozen desktop
                    dc.DrawImage(_session.Snapshot.Image, virtualRect);

                    bool hasSel = _session.HasSelection && !_session.Selection.IsEmpty;
                    Rect sel = hasSel ? _session.ClampedSelection() : Rect.Empty;

                    // dim everything except the selection
                    var dim = new SolidColorBrush(Color.FromArgb(hasSel ? (byte)0x70 : (byte)0x50, 0, 0, 0));
                    dim.Freeze();
                    if (hasSel && sel.Width > 0 && sel.Height > 0)
                    {
                        var geo = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(mon), new RectangleGeometry(sel));
                        geo.Freeze();
                        dc.DrawGeometry(dim, null, geo);
                    }
                    else
                    {
                        dc.DrawRectangle(dim, null, mon);
                    }

                    // hovering window highlight
                    if (!hasSel && _session.HasHoverWindow)
                    {
                        var hover = _session.HoverWindow;
                        var fill = new SolidColorBrush(Color.FromArgb(0x22, 0x21, 0x96, 0xF3));
                        fill.Freeze();
                        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x21, 0x96, 0xF3)), 1 * S);
                        pen.Freeze();
                        dc.DrawRectangle(fill, pen, hover);
                        DrawSizeChip(dc, hover, S, false);
                    }

                    // annotations (clipped to the selection)
                    var clip = hasSel && sel.Width > 0 ? Rect.Intersect(sel, mon) : mon;
                    if (clip.Width > 0 && clip.Height > 0)
                    {
                        dc.PushClip(new RectangleGeometry(clip));
                        foreach (var a in _session.Annotations) a.Render(dc, S);
                        if (_session.InProgress != null) _session.InProgress.Render(dc, S);
                        if (_session.EditingText != null)
                        {
                            var b = _session.EditingText.Bounds;
                            var caret = new Pen(Brushes.White, 1.5 * S);
                            caret.Freeze();
                            dc.DrawLine(caret, new Point(b.Right + 1, b.Top), new Point(b.Right + 1, b.Bottom));
                        }
                        dc.Pop();
                    }

                    if (hasSel && sel.Width > 0 && sel.Height > 0) DrawSelectionFrame(dc, sel, S);

                    DrawMagnifier(dc, S);

                    if (!hasSel) DrawHint(dc, S);
                }
                finally
                {
                    dc.Pop();
                    dc.Pop();
                }
            }

            private void DrawSelectionFrame(DrawingContext dc, Rect sel, double S)
            {
                var black = new Pen(Brushes.Black, 3 * S);
                black.Freeze();
                var white = new Pen(Brushes.White, 1 * S);
                white.Freeze();
                dc.DrawRectangle(null, black, sel);
                dc.DrawRectangle(null, white, sel);

                double hs = 6 * S;
                var handleFill = Brushes.White;
                var handlePen = new Pen(Brushes.Black, 1 * S);
                handlePen.Freeze();
                double cx = sel.Left + sel.Width / 2;
                double cy = sel.Top + sel.Height / 2;
                var pts = new[]
                {
                    new Point(sel.Left, sel.Top), new Point(cx, sel.Top), new Point(sel.Right, sel.Top),
                    new Point(sel.Right, cy), new Point(sel.Right, sel.Bottom),
                    new Point(cx, sel.Bottom), new Point(sel.Left, sel.Bottom), new Point(sel.Left, cy)
                };
                foreach (var p in pts)
                    dc.DrawRectangle(handleFill, handlePen, new Rect(p.X - hs / 2, p.Y - hs / 2, hs, hs));

                DrawSizeChip(dc, sel, S, true);
            }

            private void DrawSizeChip(DrawingContext dc, Rect r, double S, bool insideWhenTight)
            {
                var mon = Geom.ToRect(_monitor.Bounds);
                string text = string.Format(CultureInfo.InvariantCulture, "{0} × {1}", (int)Math.Round(r.Width), (int)Math.Round(r.Height));
                var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Fonts.Ui, 12 * S, Brushes.White, S);
                double padX = 6 * S, padY = 3 * S;
                double x = r.Left;
                double y = r.Top - ft.Height - padY * 2 - 3 * S;
                if (y < mon.Top + 2)
                {
                    y = insideWhenTight ? r.Top + 3 * S : r.Bottom + 3 * S;
                    if (y + ft.Height + padY * 2 > mon.Bottom) y = Math.Max(mon.Top + 2, r.Bottom - ft.Height - padY * 2 - 3 * S);
                }
                if (x + ft.Width + padX * 2 > mon.Right) x = mon.Right - ft.Width - padX * 2 - 2;
                if (x < mon.Left + 2) x = mon.Left + 2;

                var box = new Rect(x, y, ft.Width + padX * 2, ft.Height + padY * 2);
                var bg = new SolidColorBrush(Color.FromArgb(0xE0, 0x1B, 0x1D, 0x21));
                bg.Freeze();
                dc.DrawRoundedRectangle(bg, null, box, 3 * S, 3 * S);
                dc.DrawText(ft, new Point(box.X + padX, box.Y + padY));
            }

            private void DrawHint(DrawingContext dc, double S)
            {
                var mon = Geom.ToRect(_monitor.Bounds);
                string text = "拖动鼠标选择区域 · 单击选中窗口 · 右键/Esc 退出";
                var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Fonts.Ui, 13 * S, Brushes.White, S);
                double padX = 12 * S, padY = 7 * S;
                double x = mon.Left + (mon.Width - ft.Width - padX * 2) / 2;
                double y = mon.Bottom - ft.Height - padY * 2 - 40 * S;
                var box = new Rect(x, y, ft.Width + padX * 2, ft.Height + padY * 2);
                var bg = new SolidColorBrush(Color.FromArgb(0xD0, 0x1B, 0x1D, 0x21));
                bg.Freeze();
                dc.DrawRoundedRectangle(bg, null, box, 6 * S, 6 * S);
                dc.DrawText(ft, new Point(box.X + padX, box.Y + padY));
            }

            private void DrawMagnifier(DrawingContext dc, double S)
            {
                var settings = _session.Settings;
                if (!settings.ShowMagnifier) return;
                if (_session.Phase == CapturePhase.Annotating) return;

                var mon = Geom.ToRect(_monitor.Bounds);
                var c = _session.CursorGlobal;
                if (c.X < mon.Left || c.X > mon.Right || c.Y < mon.Top || c.Y > mon.Bottom) return;

                var v = Geom.ToRect(_session.VirtualBounds);
                double box = 140 * S;
                double gap = 18 * S;
                double infoH = 24 * S;

                double x = c.X + gap;
                double y = c.Y + gap;
                if (x + box > mon.Right) x = c.X - gap - box;
                if (y + box + infoH > mon.Bottom) y = c.Y - gap - box - infoH;
                if (x < mon.Left + 2) x = mon.Left + 2;
                if (y < mon.Top + 2) y = mon.Top + 2;
                var dest = new Rect(x, y, box, box);

                double srcSize = Math.Max(2, box / Math.Max(1.5, MagnifierZoom));
                double sx = c.X - v.X - srcSize / 2;
                double sy = c.Y - v.Y - srcSize / 2;
                if (sx < 0) sx = 0;
                if (sy < 0) sy = 0;
                if (sx + srcSize > v.Width) sx = Math.Max(0, v.Width - srcSize);
                if (sy + srcSize > v.Height) sy = Math.Max(0, v.Height - srcSize);
                var src = new Rect(sx, sy, Math.Min(srcSize, v.Width), Math.Min(srcSize, v.Height));
                if (src.Width < 1 || src.Height < 1) return;

                // DrawingContext has no (ImageSource, dest, src) overload: map the source rect
                // onto dest with a matrix and clip to dest instead.
                double k = dest.Width / src.Width;
                dc.PushClip(new RectangleGeometry(dest));
                dc.PushTransform(new MatrixTransform(k, 0, 0, k, dest.X - src.X * k, dest.Y - src.Y * k));
                dc.DrawImage(_session.Snapshot.Image, v);
                dc.Pop();
                dc.Pop();

                // mark the pixel under the cursor
                double fx = ((c.X - v.X) - src.X) / src.Width;
                double fy = ((c.Y - v.Y) - src.Y) / src.Height;
                var mark = new Point(dest.X + fx * dest.Width, dest.Y + fy * dest.Height);
                double px = dest.Width / src.Width;
                var markerPen = new Pen(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0x3B, 0x30)), Math.Max(1, 1 * S));
                markerPen.Freeze();
                dc.DrawRectangle(null, markerPen, new Rect(mark.X - px / 2, mark.Y - px / 2, px, px));
                var cross = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                cross.Freeze();
                dc.DrawLine(new Pen(cross, 1 * S), new Point(dest.Left, mark.Y), new Point(dest.Right, mark.Y));
                dc.DrawLine(new Pen(cross, 1 * S), new Point(mark.X, dest.Top), new Point(mark.X, dest.Bottom));

                var border = new Pen(Brushes.Black, 2 * S);
                border.Freeze();
                dc.DrawRectangle(null, border, dest);
                var inner = new Pen(Brushes.White, 1 * S);
                inner.Freeze();
                dc.DrawRectangle(null, inner, new Rect(dest.X + S, dest.Y + S, dest.Width - 2 * S, dest.Height - 2 * S));

                // info strip
                string info = string.Format(CultureInfo.InvariantCulture, "{0}, {1}", (int)Math.Round(c.X), (int)Math.Round(c.Y));
                if (settings.MagnifierShowColor)
                {
                    string hex = "";
                    try
                    {
                        var col = _session.Snapshot.GetPixel((int)Math.Round(c.X), (int)Math.Round(c.Y));
                        hex = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", col.R, col.G, col.B);
                    }
                    catch
                    {
                        hex = "";
                    }
                    if (hex.Length > 0) info += "   " + hex;
                }
                var ft = new FormattedText(info, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Fonts.Mono, 12 * S, Brushes.White, S);
                double padX = 6 * S, padY = 4 * S;
                var infoBox = new Rect(dest.Left, dest.Bottom + 3 * S, ft.Width + padX * 2, ft.Height + padY * 2);
                if (infoBox.Right > mon.Right) infoBox.X = mon.Right - infoBox.Width - 2;
                if (infoBox.Bottom > mon.Bottom) infoBox.Y = dest.Top - infoBox.Height - 3 * S;
                var infoBg = new SolidColorBrush(Color.FromArgb(0xE0, 0x1B, 0x1D, 0x21));
                infoBg.Freeze();
                dc.DrawRoundedRectangle(infoBg, null, infoBox, 3 * S, 3 * S);
                dc.DrawText(ft, new Point(infoBox.X + padX, infoBox.Y + padY));
            }
        }
    }
}