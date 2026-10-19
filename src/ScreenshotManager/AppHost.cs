using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenshotManager.Interop;
using ScreenshotManager.Models;
using ScreenshotManager.Services;
using ScreenshotManager.Views;

namespace ScreenshotManager
{
    /// <summary>
    /// 应用宿主：把设置/历史/托盘/全局热键与各窗口接在一起。
    /// 托盘菜单与热键的含义都走这里：区域截图、全屏截图、粘贴到当前窗口、贴图、历史、设置、退出。
    /// </summary>
    internal sealed class AppHost : IDisposable
    {
        private const string ActionCapture = "capture";
        private const string ActionCaptureFull = "captureFull";
        private const string ActionPaste = "paste";
        private const string ActionHistory = "history";
        private const string ActionPinClipboard = "pinClipboard";

        private readonly Application _app;
        private readonly SettingsService _settings;
        private readonly HistoryService _history = new HistoryService();
        private readonly TrayService _tray;
        private readonly HotkeyService _hotkeys = new HotkeyService();

        private List<CaptureOverlayWindow> _overlays;
        private HistoryWindow _historyWindow;
        private SettingsWindow _settingsWindow;
        private bool _capturing;
        private bool _disposed;

        public AppHost(Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _settings = new SettingsService();
            _tray = new TrayService();

            _history.Load();
            _history.Prune(_settings.Settings.HistoryMaxCount, _settings.Settings.HistoryMaxAgeDays, _settings.Settings.SaveDirectory);

            _tray.RegionCaptureRequested += StartRegionCapture;
            _tray.FullScreenCaptureRequested += CaptureFullScreen;
            _tray.PasteRequested += PasteToFocusedWindow;
            _tray.PinClipboardRequested += PinFromClipboard;
            _tray.HistoryRequested += ShowHistory;
            _tray.SettingsRequested += ShowSettings;
            _tray.QuitRequested += Quit;
            _tray.StartupToggled += OnStartupToggled;

            _hotkeys.Pressed += OnHotkeyPressed;
            RegisterHotkeys();

            _tray.SyncStartup(StartupService.IsEnabled());
            _tray.UpdateMenuHotkeys(_settings.Settings);
            _settings.Changed += OnSettingsChanged;

            AppLog.Info("初始化完成：历史 " + _history.Count + " 条");
        }

        // ------------------------------------------------------------ 热键分发

        private void OnHotkeyPressed(string action)
        {
            AppLog.Info("热键触发: " + action);
            switch (action)
            {
                case ActionCapture:
                    StartRegionCapture();
                    break;
                case ActionCaptureFull:
                    CaptureFullScreen();
                    break;
                case ActionPaste:
                    PasteToFocusedWindow();
                    break;
                case ActionHistory:
                    ShowHistory();
                    break;
                case ActionPinClipboard:
                    PinFromClipboard();
                    break;
            }
        }

        private void RegisterHotkeys()
        {
            var s = _settings.Settings;
            RegisterHotkey(ActionCapture, s.HotkeyCapture);
            RegisterHotkey(ActionCaptureFull, s.HotkeyCaptureFullScreen);
            RegisterHotkey(ActionPaste, s.HotkeyPaste);
            RegisterHotkey(ActionHistory, s.HotkeyHistory);
            RegisterHotkey(ActionPinClipboard, s.HotkeyPinClipboard);
        }

        private void RegisterHotkey(string action, string gesture)
        {
            string error;
            if (!_hotkeys.Register(action, gesture, out error))
            {
                AppLog.Warn("热键注册失败: " + error);
                if (_settings.Settings.ShowTrayNotifications)
                {
                    _tray.Notify("热键注册失败", error);
                }
            }
        }

        // ------------------------------------------------------------ 截图

        private void StartRegionCapture()
        {
            if (_capturing)
            {
                return;
            }

            ScreenSnapshot snapshot;
            try
            {
                snapshot = ScreenCaptureService.CaptureVirtualScreen();
            }
            catch (Exception ex)
            {
                AppLog.Error("屏幕捕获失败", ex);
                _tray.Notify("截图失败", ex.Message);
                return;
            }

            try
            {
                _capturing = true;
                var session = new CaptureSession(snapshot, _settings.Settings);
                _overlays = new List<CaptureOverlayWindow>();

                session.ConfirmRequested += () =>
                {
                    var image = session.ComposeResult();
                    CloseOverlays();
                    ApplyCaptureRules(image);
                };
                session.CopyRequested += () => CopyImage(session.ComposeResult());
                session.SaveRequested += () => SaveImage(session.ComposeResult());
                session.PinRequested += () => PinImage(session.ComposeResult());
                session.CancelRequested += CloseOverlays;

                // 每个显示器一个覆盖窗口，它们共享同一个 session，选区可以跨屏。
                foreach (var monitor in MonitorInfo.Enumerate())
                {
                    _overlays.Add(new CaptureOverlayWindow(session, monitor));
                }

                if (_overlays.Count == 0)
                {
                    CloseOverlays();
                    _tray.Notify("截图失败", "没有检测到显示器");
                    return;
                }

                foreach (var overlay in _overlays)
                {
                    overlay.Show();
                }
                AppLog.Info("区域截图开始，覆盖 " + _overlays.Count + " 个显示器");
            }
            catch (Exception ex)
            {
                AppLog.Error("启动区域截图失败", ex);
                CloseOverlays();
                _tray.Notify("截图失败", ex.Message);
            }
        }

        private void CloseOverlays()
        {
            _capturing = false;
            var overlays = _overlays;
            _overlays = null;
            if (overlays == null)
            {
                return;
            }
            foreach (var overlay in overlays)
            {
                try
                {
                    overlay.Close();
                }
                catch (Exception ex)
                {
                    AppLog.Warn("关闭覆盖窗口失败: " + ex.Message);
                }
            }
        }

        private void CaptureFullScreen()
        {
            if (_capturing)
            {
                return;
            }
            try
            {
                var snapshot = ScreenCaptureService.CaptureVirtualScreen();
                ApplyCaptureRules(snapshot.Image);
            }
            catch (Exception ex)
            {
                AppLog.Error("全屏截图失败", ex);
                _tray.Notify("截图失败", ex.Message);
            }
        }

        /// <summary>按设置决定截图完成后要做什么（复制 / 保存 / 贴图）。</summary>
        private void ApplyCaptureRules(BitmapSource image)
        {
            if (image == null)
            {
                return;
            }
            var s = _settings.Settings;
            if (s.AutoCopyToClipboard)
            {
                CopyImage(image);
            }
            if (s.AutoSaveOnCopy)
            {
                SaveImage(image);
            }
            if (s.PinAfterCapture)
            {
                PinImage(image);
            }
            if (!s.AutoCopyToClipboard && !s.AutoSaveOnCopy && !s.PinAfterCapture && s.ShowTrayNotifications)
            {
                _tray.Notify("截图完成", "未启用自动复制/保存/贴图，本次截图已丢弃");
            }
        }

        // ------------------------------------------------------------ 剪贴板与贴图

        private void CopyImage(BitmapSource image)
        {
            if (image == null)
            {
                return;
            }
            try
            {
                if (ClipboardService.SetImage(image) && _settings.Settings.ShowTrayNotifications)
                {
                    _tray.Notify("已复制", "截图已复制到剪贴板");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("复制到剪贴板失败", ex);
            }
        }

        /// <summary>保存并计入历史；返回落盘路径（失败返回 null）。</summary>
        private string SaveImage(BitmapSource image)
        {
            if (image == null)
            {
                return null;
            }
            var s = _settings.Settings;
            string name = FileNames.BuildName(s, "Shot");
            string format = ImageComposer.NormalizeExtension(s.ImageFormat);

            string label = "已保存";
            string path = TrySave(image, s.SaveDirectory, name, format, s.JpegQuality, out string error);
            if (path == null)
            {
                // 配置的保存目录不可写（受限沙箱、目录被删、权限变更）时退回配置目录内的 Screenshots，
                // 不让这次截图白丢。
                AppLog.Warn("保存目录不可写，改用兜底目录 " + AppPaths.ScreenshotsDir + " :: " + error);
                label = "已保存（兜底目录）";
                path = TrySave(image, AppPaths.ScreenshotsDir, name, format, s.JpegQuality, out error);
            }
            if (path == null)
            {
                _tray.Notify("保存失败", error);
                return null;
            }

            try
            {
                _history.Add(path, image.PixelWidth, image.PixelHeight, "capture");
                _history.Save();
            }
            catch (Exception ex)
            {
                AppLog.Error("写入历史失败", ex);
            }

            if (s.ShowTrayNotifications)
            {
                _tray.Notify(label, path);
            }
            return path;
        }

        /// <summary>单目录保存尝试；失败时把原因放进 <paramref name="error"/>，不抛异常。</summary>
        private static string TrySave(BitmapSource image, string directory, string name, string format, int jpegQuality, out string error)
        {
            error = null;
            try
            {
                return ImageComposer.Save(image, directory, name, format, jpegQuality);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLog.Error("保存截图失败 " + directory, ex);
                return null;
            }
        }

        private void PinImage(BitmapSource image)
        {
            if (image == null)
            {
                return;
            }
            var monitor = MonitorInfo.Primary();
            var bounds = new Rect(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, monitor.Bounds.Height);
            new PinWindow(image, bounds, _settings.Settings).Show();
        }

        private void PinFromClipboard()
        {
            try
            {
                var image = ClipboardService.GetImage();
                if (image == null)
                {
                    _tray.Notify("贴图失败", "剪贴板中没有图片");
                    return;
                }
                PinImage(image);
            }
            catch (Exception ex)
            {
                AppLog.Error("贴图失败", ex);
                _tray.Notify("贴图失败", ex.Message);
            }
        }

        /// <summary>把剪贴板里的图片粘贴进当前前台窗口（恢复焦点后发 Ctrl+V）。</summary>
        private void PasteToFocusedWindow()
        {
            try
            {
                var image = ClipboardService.GetImage();
                if (image == null)
                {
                    _tray.Notify("粘贴失败", "剪贴板中没有图片");
                    return;
                }

                IntPtr target = Native.GetForegroundWindow();
                if (!ClipboardService.SetImage(image))
                {
                    _tray.Notify("粘贴失败", "无法写入剪贴板");
                    return;
                }
                if (target == IntPtr.Zero)
                {
                    return;
                }

                Native.SetForegroundWindow(target);
                Thread.Sleep(120);
                System.Windows.Forms.SendKeys.SendWait("^v");
            }
            catch (Exception ex)
            {
                AppLog.Error("粘贴到当前窗口失败", ex);
                _tray.Notify("粘贴失败", ex.Message);
            }
        }

        // ------------------------------------------------------------ 窗口

        private void ShowHistory()
        {
            if (_historyWindow == null)
            {
                var window = new HistoryWindow(_history, _settings.Settings);
                window.CopyRequested += item => CopyImage(LoadItemImage(item));
                window.PinRequested += item => PinImage(LoadItemImage(item));
                window.Closed += (s, e) =>
                {
                    if (ReferenceEquals(_historyWindow, window))
                    {
                        _historyWindow = null;
                    }
                };
                _historyWindow = window;
            }
            _historyWindow.Show();
            _historyWindow.Activate();
            _historyWindow.Refresh();
        }

        private void ShowSettings()
        {
            if (_settingsWindow == null)
            {
                var window = new SettingsWindow(_settings);
                window.Closed += (s, e) =>
                {
                    if (ReferenceEquals(_settingsWindow, window))
                    {
                        _settingsWindow = null;
                    }
                };
                _settingsWindow = window;
            }
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        /// <summary>诊断用（环境变量 SM_SELFTEST=1 触发）：依次打开历史/设置窗口并读写设置，记录成功或异常。</summary>
        internal void RunSelfTest()
        {
            AppLog.Info("自检开始");
            try
            {
                ShowHistory();
                AppLog.Info("自检：历史窗口已打开");
            }
            catch (Exception ex)
            {
                AppLog.Error("自检：历史窗口失败", ex);
            }

            try
            {
                ShowSettings();
                AppLog.Info("自检：设置窗口已打开");
            }
            catch (Exception ex)
            {
                AppLog.Error("自检：设置窗口失败", ex);
            }

            try
            {
                AppSettings current = _settings.Settings;
                current.Normalize();
                _settings.Save();
                AppLog.Info("自检：设置保存成功 -> " + AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                AppLog.Error("自检：设置保存失败", ex);
            }

            AppLog.Info("自检结束");
        }

        private static BitmapSource LoadItemImage(ScreenshotItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.FilePath))
            {
                return null;
            }
            try
            {
                return ImageComposer.LoadFromFile(item.FilePath);
            }
            catch (Exception ex)
            {
                AppLog.Error("读取历史图片失败: " + item.FilePath, ex);
                return null;
            }
        }

        // ------------------------------------------------------------ 设置 / 退出

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            try
            {
                _tray.UpdateMenuHotkeys(_settings.Settings);
                RegisterHotkeys();
                _tray.SyncStartup(StartupService.IsEnabled());
                if (_historyWindow != null)
                {
                    // 历史窗口持有旧的 AppSettings 快照，重开一次最省事也最不会错。
                    _historyWindow.Close();
                    _historyWindow = null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("应用设置变更失败", ex);
            }
        }

        private void OnStartupToggled(bool enabled)
        {
            string error;
            if (!StartupService.Set(enabled, out error))
            {
                AppLog.Warn("开机启动设置失败: " + error);
                _tray.Notify("设置失败", string.IsNullOrEmpty(error) ? "无法修改开机启动项" : error);
                _tray.SyncStartup(StartupService.IsEnabled());
                return;
            }
            _settings.Update(s => s.StartWithWindows = enabled);
        }

        private void Quit()
        {
            AppLog.Info("收到退出请求");
            CloseOverlays();
            _app.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                _settings.Changed -= OnSettingsChanged;
                _hotkeys.Pressed -= OnHotkeyPressed;
                CloseOverlays();

                if (_settingsWindow != null)
                {
                    _settingsWindow.Close();
                    _settingsWindow = null;
                }
                if (_historyWindow != null)
                {
                    _historyWindow.Close();
                    _historyWindow = null;
                }

                _history.Save();
                _hotkeys.Dispose();
                _tray.Dispose();
                AppLog.Info("已释放全部资源");
            }
            catch (Exception ex)
            {
                AppLog.Warn("释放资源时出错: " + ex.Message);
            }
        }
    }
}