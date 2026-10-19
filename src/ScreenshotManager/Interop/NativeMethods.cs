using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenshotManager.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public System.Drawing.Rectangle ToRectangle() => System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    internal static class Native
    {
        // ---------- constants ----------
        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        public const uint GW_HWNDNEXT = 2;
        public const int GA_ROOT = 2;

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const uint MONITOR_DEFAULTTOPRIMARY = 1;

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;

        // 扩展窗口样式 / 分层窗口属性：用来识别"用户根本看不见"的覆盖层
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int LWA_COLORKEY = 0x00000001;
        public const int LWA_ALPHA = 0x00000002;

        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        // ---------- user32 ----------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, int gaFlags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        /// <summary>窗口类名（取不到时返回空串）。</summary>
        public static string GetWindowClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            try
            {
                var buffer = new StringBuilder(256);
                int length = GetClassName(hwnd, buffer, buffer.Capacity);
                return length > 0 ? buffer.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 桌面/任务栏这类"假窗口"：它们铺满整个屏幕，绝不能被当成"鼠标下的窗口"，
        /// 否则悬停高亮会盖住整屏、拖拽也会被当成"在整屏矩形上调整角点"。
        /// </summary>
        public static bool IsShellWindow(IntPtr hwnd)
        {
            string cls = GetWindowClassName(hwnd);
            switch (cls)
            {
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "TaskListThumbnailWnd":
                case "SysShadow":
                case "ForegroundStaging":
                case "MultitaskingViewFrame":
                case "XamlExplorerHostIslandWindow":
                    return true;
                default:
                    return false;
            }
        }

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool GetKeyboardState(byte[] lpKeyState);

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        // ---------- shcore ----------
        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        // ---------- dwmapi ----------
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        // ---------- helpers ----------

        public static bool TryGetCursorPos(out System.Drawing.Point p)
        {
            if (GetCursorPos(out POINT pt))
            {
                p = new System.Drawing.Point(pt.X, pt.Y);
                return true;
            }
            p = System.Drawing.Point.Empty;
            return false;
        }

        /// <summary>True extended frame bounds (no invisible resize border), falls back to GetWindowRect.</summary>
        public static bool TryGetWindowBounds(IntPtr hwnd, out System.Drawing.Rectangle bounds)
        {
            bounds = System.Drawing.Rectangle.Empty;
            if (hwnd == IntPtr.Zero) return false;
            RECT r;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0
                && r.Width > 0 && r.Height > 0)
            {
                bounds = r.ToRectangle();
                return true;
            }
            if (GetWindowRect(hwnd, out r) && r.Width > 0 && r.Height > 0)
            {
                bounds = r.ToRectangle();
                return true;
            }
            return false;
        }

        public static bool IsWindowCloaked(IntPtr hwnd)
        {
            int cloaked;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) != 0) return false;
            return cloaked != 0;
        }

        public static System.Drawing.Rectangle GetVirtualScreenBounds()
        {
            return System.Drawing.Rectangle.FromLTRB(
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN) + GetSystemMetrics(SM_CYVIRTUALSCREEN));
        }

        public static uint CurrentProcessId => (uint)Process.GetCurrentProcess().Id;

        /// <summary>
        /// 用户其实看不见的"透明覆盖层"：NVIDIA/游戏/输入法 HUD 这类窗口
        /// <see cref="IsWindowVisible"/> 返回 true、也铺满屏幕，但整窗 alpha=0 或点击穿透。
        /// 典型例子：NVIDIA GeForce Overlay 的 `CEF-OSC-WIDGET`（TOPMOST|TRANSPARENT|TOOLWINDOW|LAYERED，alpha=0）。
        /// 这样的窗口绝不能当成"鼠标下的窗口"，否则悬停高亮永远是整屏、真实窗口永远检测不到。
        /// </summary>
        public static bool IsTransparentOverlay(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TRANSPARENT) != 0) return true;

            if ((exStyle & WS_EX_LAYERED) != 0)
            {
                uint key;
                byte alpha;
                uint flags;
                if (GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags) && (flags & LWA_ALPHA) != 0 && alpha == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Topmost visible window that contains the point and does not belong to <paramref name="excludePid"/>.
        /// Needed because our own topmost capture overlay covers the whole screen.
        /// Desktop/taskbar-class windows and anything covering the whole virtual screen are skipped:
        /// otherwise "the window under the cursor" is just the desktop, and drag-to-select degenerates
        /// into resizing a full-screen rectangle.
        /// </summary>
        public static IntPtr GetWindowUnderPoint(System.Drawing.Point point, uint excludePid)
        {
            POINT pt = new POINT(point.X, point.Y);
            System.Drawing.Rectangle virtualBounds = GetVirtualScreenBounds();
            long virtualArea = Math.Max(1L, (long)virtualBounds.Width * virtualBounds.Height);
            IntPtr hwnd = GetTopWindow(IntPtr.Zero);
            int guard = 0;
            while (hwnd != IntPtr.Zero && guard++ < 4096)
            {
                if (IsWindowVisible(hwnd) && !IsIconic(hwnd) && !IsWindowCloaked(hwnd) && !IsTransparentOverlay(hwnd))
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid != excludePid && !IsShellWindow(hwnd))
                    {
                        System.Drawing.Rectangle b;
                        if (TryGetWindowBounds(hwnd, out b) && b.Width > 1 && b.Height > 1
                            && point.X >= b.X && point.X < b.Right && point.Y >= b.Y && point.Y < b.Bottom
                            && (long)b.Width * b.Height < virtualArea * 97 / 100)
                        {
                            return hwnd;
                        }
                    }
                }
                hwnd = GetWindow(hwnd, GW_HWNDNEXT);
            }
            return IntPtr.Zero;
        }
    }

    /// <summary>One physical monitor, with its scale factor.</summary>
    public sealed class MonitorInfo
    {
        public IntPtr Handle;
        public System.Drawing.Rectangle Bounds;   // physical pixels, virtual-screen coords
        public System.Drawing.Rectangle WorkArea;
        public bool IsPrimary;
        public int DpiX = 96;
        public int DpiY = 96;

        public double Scale => DpiX / 96.0;
        public int Width => Bounds.Width;
        public int Height => Bounds.Height;

        public override string ToString() => $"Monitor {Bounds} dpi={DpiX} primary={IsPrimary}";

        public static List<MonitorInfo> Enumerate()
        {
            var list = new List<MonitorInfo>();
            Native.MonitorEnumProc proc = (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!Native.GetMonitorInfo(hMonitor, ref mi)) return true;

                var info = new MonitorInfo
                {
                    Handle = hMonitor,
                    Bounds = mi.rcMonitor.ToRectangle(),
                    WorkArea = mi.rcWork.ToRectangle(),
                    IsPrimary = (mi.dwFlags & 1) != 0
                };
                uint dx = 96, dy = 96;
                try
                {
                    if (Native.GetDpiForMonitor(hMonitor, 0 /*MDT_EFFECTIVE_DPI*/, out dx, out dy) != 0)
                    {
                        dx = 96; dy = 96;
                    }
                }
                catch
                {
                    dx = 96; dy = 96;
                }
                if (dx == 0) dx = 96;
                if (dy == 0) dy = 96;
                info.DpiX = (int)dx;
                info.DpiY = (int)dy;
                list.Add(info);
                return true;
            };

            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            if (list.Count == 0)
            {
                // Extremely defensive fallback: treat the virtual screen as a single 96-dpi monitor.
                list.Add(new MonitorInfo
                {
                    Handle = IntPtr.Zero,
                    Bounds = Native.GetVirtualScreenBounds(),
                    WorkArea = Native.GetVirtualScreenBounds(),
                    IsPrimary = true
                });
            }
            list.Sort((a, b) => a.Bounds.X != b.Bounds.X ? a.Bounds.X.CompareTo(b.Bounds.X) : a.Bounds.Y.CompareTo(b.Bounds.Y));
            return list;
        }

        public static MonitorInfo FromPoint(System.Drawing.Point p)
        {
            MonitorInfo best = null;
            foreach (var m in Enumerate())
            {
                if (p.X >= m.Bounds.X && p.X < m.Bounds.Right && p.Y >= m.Bounds.Y && p.Y < m.Bounds.Bottom)
                {
                    return m;
                }
            }
            return best ?? (Enumerate().Count > 0 ? Enumerate()[0] : null);
        }

        public static MonitorInfo Primary()
        {
            foreach (var m in Enumerate()) if (m.IsPrimary) return m;
            var all = Enumerate();
            return all.Count > 0 ? all[0] : null;
        }

        public static MonitorInfo FromHandle(IntPtr hwnd)
        {
            uint dpi = Native.GetDpiForWindow(hwnd);
            System.Drawing.Rectangle b;
            if (!Native.TryGetWindowBounds(hwnd, out b)) b = System.Drawing.Rectangle.Empty;
            MonitorInfo found = null;
            foreach (var m in Enumerate())
            {
                if (b.Width > 0 && b.X >= m.Bounds.X - 2 && b.X < m.Bounds.Right && b.Y >= m.Bounds.Y - 2 && b.Y < m.Bounds.Bottom)
                    found = m;
            }
            if (found != null && dpi > 0) { found.DpiX = (int)dpi; found.DpiY = (int)dpi; }
            return found;
        }
    }
}