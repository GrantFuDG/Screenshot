using System;
using System.Drawing;
using System.Windows.Forms;
using ScreenshotManager.Models;

namespace ScreenshotManager.Services
{
    /// <summary>Tray icon + context menu (WinForms NotifyIcon hosted by the WPF message pump).</summary>
    public sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _startupItem;
        private readonly Icon _appIcon;
        private bool _disposed;

        public event Action RegionCaptureRequested;
        public event Action FullScreenCaptureRequested;
        public event Action PasteRequested;
        public event Action PinClipboardRequested;
        public event Action HistoryRequested;
        public event Action SettingsRequested;
        public event Action QuitRequested;
        public event Action<bool> StartupToggled;

        public TrayService()
        {
            _appIcon = IconFactory.CreateTrayIcon(32);
            _menu = new ContextMenuStrip { ShowImageMargin = false };
            _menu.Font = new Font("Microsoft YaHei UI", 9f);

            var region = new ToolStripMenuItem("区域截图") { ShortcutKeyDisplayString = "F1" };
            region.Click += (s, e) => Fire(RegionCaptureRequested);

            var full = new ToolStripMenuItem("全屏截图") { ShortcutKeyDisplayString = "Ctrl+F1" };
            full.Click += (s, e) => Fire(FullScreenCaptureRequested);

            var pin = new ToolStripMenuItem("贴图到屏幕") { ShortcutKeyDisplayString = "F3" };
            pin.Click += (s, e) => Fire(PinClipboardRequested);

            var paste = new ToolStripMenuItem("粘贴到当前窗口") { ShortcutKeyDisplayString = "Shift+F3" };
            paste.Click += (s, e) => Fire(PasteRequested);

            var history = new ToolStripMenuItem("截图历史…");
            history.Click += (s, e) => Fire(HistoryRequested);

            var settings = new ToolStripMenuItem("设置…");
            settings.Click += (s, e) => Fire(SettingsRequested);

            _startupItem = new ToolStripMenuItem("开机自动启动") { CheckOnClick = true };
            _startupItem.Click += (s, e) =>
            {
                bool want = _startupItem.Checked;
                var handler = StartupToggled;
                if (handler != null) handler(want);
            };

            var quit = new ToolStripMenuItem("退出");
            quit.Click += (s, e) => Fire(QuitRequested);

            _menu.Items.Add(region);
            _menu.Items.Add(full);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(pin);
            _menu.Items.Add(paste);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(history);
            _menu.Items.Add(settings);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_startupItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(quit);

            _icon = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "截图管家 - 截图、贴图、历史",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.DoubleClick += (s, e) => Fire(RegionCaptureRequested);
        }

        private static void Fire(Action handler)
        {
            var h = handler;
            if (h != null) h();
        }

        public void SyncStartup(bool enabled)
        {
            _startupItem.Checked = enabled;
        }

        public void UpdateMenuHotkeys(AppSettings settings)
        {
            if (settings == null || _menu.Items.Count < 5) return;
            ((ToolStripMenuItem)_menu.Items[0]).ShortcutKeyDisplayString = settings.HotkeyCapture;
            ((ToolStripMenuItem)_menu.Items[1]).ShortcutKeyDisplayString = settings.HotkeyCaptureFullScreen;
            ((ToolStripMenuItem)_menu.Items[3]).ShortcutKeyDisplayString = settings.HotkeyPinClipboard;
            ((ToolStripMenuItem)_menu.Items[4]).ShortcutKeyDisplayString = settings.HotkeyPaste;
        }

        public void Notify(string title, string text)
        {
            try
            {
                _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AppLog.Warn("托盘通知失败: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
                _menu.Dispose();
                _appIcon.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn("释放托盘图标失败: " + ex.Message);
            }
        }
    }
}