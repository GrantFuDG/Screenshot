using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenshotManager.Models;

namespace ScreenshotManager.Views
{
    /// <summary>SettingsWindow 的分组构建 / 读取 / 回填（配合 SettingsWindow.cs）。</summary>
    public sealed partial class SettingsWindow
    {
        private TextBox _hkCapture;
        private TextBox _hkCaptureFull;
        private TextBox _hkPaste;
        private TextBox _hkHistory;
        private TextBox _hkPin;

        private TextBox _saveDir;
        private TextBox _fileNameTemplate;
        private ComboBox _imageFormat;
        private TextBox _jpegQuality;
        private CheckBox _autoSave;
        private CheckBox _autoCopy;
        private CheckBox _pinAfterCapture;

        private TextBox _historyMaxCount;
        private TextBox _historyMaxAgeDays;

        private CheckBox _showMagnifier;
        private TextBox _magnifierZoom;
        private CheckBox _magnifierColor;
        private CheckBox _detectWindow;
        private CheckBox _showSize;

        private TextBox _defaultColor;
        private Border _colorPreview;
        private TextBox _defaultThickness;
        private CheckBox _mosaicBlur;
        private TextBox _mosaicBlockSize;

        private Slider _pinOpacity;
        private TextBlock _opacityLabel;
        private CheckBox _hidePins;
        private CheckBox _pinTopmost;

        private CheckBox _startWithWindows;
        private CheckBox _trayNotify;
        private CheckBox _minimizeToTray;

        private void BuildSections()
        {
            BuildHotkeys();
            BuildSaving();
            BuildHistory();
            BuildCapture();
            BuildAnnotation();
            BuildPin();
            BuildShell();
        }

        private void BuildHotkeys()
        {
            AddSection("快捷键");
            _hkCapture = MakeText(null, 160);
            _hkCaptureFull = MakeText(null, 160);
            _hkPaste = MakeText(null, 160);
            _hkHistory = MakeText(null, 160);
            _hkPin = MakeText(null, 160);
            AddRow("区域截图", _hkCapture);
            AddRow("全屏截图", _hkCaptureFull);
            AddRow("贴图到屏幕", _hkPaste);
            AddRow("打开截图历史", _hkHistory);
            AddRow("粘贴到当前窗口", _hkPin);
            AddNote("写法示例：F1 / Ctrl+F1 / Ctrl+Shift+H。保存后立即重新注册全局热键。");
        }

        private void BuildSaving()
        {
            AddSection("保存与输出");
            _saveDir = MakeText(null, 380);
            var browse = new Button
            {
                Content = "浏览…",
                MinWidth = 64,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(8, 0, 0, 0),
                Background = FieldBorder,
                Foreground = TextBrush,
                BorderThickness = new Thickness(0)
            };
            browse.Click += OnBrowseSaveDir;
            var dirHost = new StackPanel { Orientation = Orientation.Horizontal };
            dirHost.Children.Add(_saveDir);
            dirHost.Children.Add(browse);
            AddRow("保存目录", dirHost);

            _fileNameTemplate = MakeText(null, 240);
            AddRow("文件名模板", _fileNameTemplate, "{yyyyMMdd_HHmmss}");

            _imageFormat = MakeCombo("png", "jpg", "bmp");
            AddRow("图片格式", _imageFormat);

            _jpegQuality = MakeText(null, 80);
            AddRow("JPEG 质量", _jpegQuality, "10–100，仅 jpg 生效");

            _autoSave = MakeCheck("复制后自动保存到磁盘", true);
            AddRow("自动保存", _autoSave);
            _autoCopy = MakeCheck("截图后自动复制到剪贴板", true);
            AddRow("自动复制", _autoCopy);
            _pinAfterCapture = MakeCheck("截图完成后自动贴图", false);
            AddRow("自动贴图", _pinAfterCapture);
        }

        private void BuildHistory()
        {
            AddSection("截图历史");
            _historyMaxCount = MakeText(null, 90);
            AddRow("最多保留张数", _historyMaxCount, "10–100000");
            _historyMaxAgeDays = MakeText(null, 90);
            AddRow("过期天数", _historyMaxAgeDays, "0 = 永不过期");
        }

        private void BuildCapture()
        {
            AddSection("截图行为");
            _showMagnifier = MakeCheck("显示放大镜", true);
            AddRow("放大镜", _showMagnifier);
            _magnifierZoom = MakeText(null, 80);
            AddRow("放大倍数", _magnifierZoom, "1–16");
            _magnifierColor = MakeCheck("放大镜显示取色值", true);
            AddRow("取色显示", _magnifierColor);
            _detectWindow = MakeCheck("悬停时自动识别窗口", true);
            AddRow("窗口识别", _detectWindow);
            _showSize = MakeCheck("显示选区尺寸", true);
            AddRow("尺寸提示", _showSize);
        }

        private void BuildAnnotation()
        {
            AddSection("标注默认值");
            _defaultColor = MakeText(null, 140);
            _defaultColor.TextChanged += OnDefaultColorChanged;
            _colorPreview = new Border
            {
                Width = 26,
                Height = 20,
                Margin = new Thickness(8, 0, 0, 0),
                CornerRadius = new CornerRadius(3),
                BorderBrush = FieldBorder,
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent
            };
            var colorHost = new StackPanel { Orientation = Orientation.Horizontal };
            colorHost.Children.Add(_defaultColor);
            colorHost.Children.Add(_colorPreview);
            AddRow("默认颜色", colorHost, "#AARRGGBB");

            _defaultThickness = MakeText(null, 80);
            AddRow("默认线宽", _defaultThickness, "1–40");

            _mosaicBlur = MakeCheck("使用高斯模糊（不勾选 = 马赛克）", false);
            AddRow("马赛克方式", _mosaicBlur);

            _mosaicBlockSize = MakeText(null, 80);
            AddRow("马赛克块大小", _mosaicBlockSize, "2–64");
        }

        private void BuildPin()
        {
            AddSection("贴图");
            _pinOpacity = MakeSlider(0.1, 1.0, 1.0, 0.05);
            _opacityLabel = new TextBlock
            {
                Foreground = DimBrush,
                FontSize = 11,
                Width = 42,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _pinOpacity.ValueChanged += (s, e) => _opacityLabel.Text = Math.Round(_pinOpacity.Value * 100) + "%";
            var opacityHost = new StackPanel { Orientation = Orientation.Horizontal };
            opacityHost.Children.Add(_pinOpacity);
            opacityHost.Children.Add(_opacityLabel);
            AddRow("默认不透明度", opacityHost);

            _hidePins = MakeCheck("截图时隐藏已有贴图", true);
            AddRow("隐藏贴图", _hidePins);
            _pinTopmost = MakeCheck("贴图始终置顶", true);
            AddRow("窗口置顶", _pinTopmost);
        }

        private void BuildShell()
        {
            AddSection("系统");
            _startWithWindows = MakeCheck("开机自动启动", false);
            AddRow("开机启动", _startWithWindows);
            _trayNotify = MakeCheck("显示托盘气泡通知", true);
            AddRow("托盘通知", _trayNotify);
            _minimizeToTray = MakeCheck("关闭主窗口时仅最小化到托盘", true);
            AddRow("托盘驻留", _minimizeToTray);
            AddNote("设置保存在 " + AppPaths.SettingsFile);
        }

        // ---------------------------------------------------------------- 回填 / 读取

        private void LoadFrom(AppSettings s)
        {
            _hkCapture.Text = s.HotkeyCapture;
            _hkCaptureFull.Text = s.HotkeyCaptureFullScreen;
            _hkPaste.Text = s.HotkeyPaste;
            _hkHistory.Text = s.HotkeyHistory;
            _hkPin.Text = s.HotkeyPinClipboard;

            _saveDir.Text = s.SaveDirectory;
            _fileNameTemplate.Text = s.FileNameTemplate;
            _imageFormat.SelectedIndex = Math.Max(0, _imageFormat.Items.IndexOf(s.ImageFormat));
            _jpegQuality.Text = s.JpegQuality.ToString();
            _autoSave.IsChecked = s.AutoSaveOnCopy;
            _autoCopy.IsChecked = s.AutoCopyToClipboard;
            _pinAfterCapture.IsChecked = s.PinAfterCapture;

            _historyMaxCount.Text = s.HistoryMaxCount.ToString();
            _historyMaxAgeDays.Text = s.HistoryMaxAgeDays.ToString();

            _showMagnifier.IsChecked = s.ShowMagnifier;
            _magnifierZoom.Text = s.MagnifierZoom.ToString();
            _magnifierColor.IsChecked = s.MagnifierShowColor;
            _detectWindow.IsChecked = s.DetectWindowOnHover;
            _showSize.IsChecked = s.ShowSelectionSize;

            _defaultColor.Text = s.DefaultColor;
            _defaultThickness.Text = s.DefaultThickness.ToString();
            _mosaicBlur.IsChecked = s.MosaicBlurMode;
            _mosaicBlockSize.Text = s.MosaicBlockSize.ToString();
            SyncColorPreview();

            _pinOpacity.Value = Math.Clamp(s.PinDefaultOpacity, 0.1, 1.0);
            _opacityLabel.Text = Math.Round(_pinOpacity.Value * 100) + "%";
            _hidePins.IsChecked = s.HidePinsFromCapture;
            _pinTopmost.IsChecked = s.PinTopmost;

            _startWithWindows.IsChecked = s.StartWithWindows;
            _trayNotify.IsChecked = s.ShowTrayNotifications;
            _minimizeToTray.IsChecked = s.MinimizeToTrayOnly;
        }

        /// <summary>把界面值写进 settings；数值非法时返回 false 并提示。</summary>
        private bool ReadInto(AppSettings s)
        {
            s.HotkeyCapture = (_hkCapture.Text ?? string.Empty).Trim();
            s.HotkeyCaptureFullScreen = (_hkCaptureFull.Text ?? string.Empty).Trim();
            s.HotkeyPaste = (_hkPaste.Text ?? string.Empty).Trim();
            s.HotkeyHistory = (_hkHistory.Text ?? string.Empty).Trim();
            s.HotkeyPinClipboard = (_hkPin.Text ?? string.Empty).Trim();

            s.SaveDirectory = (_saveDir.Text ?? string.Empty).Trim();
            s.FileNameTemplate = (_fileNameTemplate.Text ?? string.Empty).Trim();
            s.ImageFormat = (_imageFormat.SelectedItem as string) ?? "png";
            if (!TryInt(_jpegQuality, 10, 100, "JPEG 质量", out int quality)) return false;
            s.JpegQuality = quality;
            s.AutoSaveOnCopy = _autoSave.IsChecked == true;
            s.AutoCopyToClipboard = _autoCopy.IsChecked == true;
            s.PinAfterCapture = _pinAfterCapture.IsChecked == true;

            if (!TryInt(_historyMaxCount, 10, 100000, "历史张数", out int maxCount)) return false;
            s.HistoryMaxCount = maxCount;
            if (!TryInt(_historyMaxAgeDays, 0, 3650, "过期天数", out int maxAge)) return false;
            s.HistoryMaxAgeDays = maxAge;

            s.ShowMagnifier = _showMagnifier.IsChecked == true;
            if (!TryInt(_magnifierZoom, 1, 16, "放大倍数", out int zoom)) return false;
            s.MagnifierZoom = zoom;
            s.MagnifierShowColor = _magnifierColor.IsChecked == true;
            s.DetectWindowOnHover = _detectWindow.IsChecked == true;
            s.ShowSelectionSize = _showSize.IsChecked == true;

            string color = (_defaultColor.Text ?? string.Empty).Trim();
            if (!TryParseColor(color, out _))
            {
                _defaultColor.Focus();
                return Fail("默认颜色不是合法颜色值，例如 #FFE53935");
            }
            s.DefaultColor = color;
            if (!TryNumber(_defaultThickness, 1, 40, "默认线宽", out double thickness)) return false;
            s.DefaultThickness = thickness;
            s.MosaicBlurMode = _mosaicBlur.IsChecked == true;
            if (!TryInt(_mosaicBlockSize, 2, 64, "马赛克块大小", out int block)) return false;
            s.MosaicBlockSize = block;

            s.PinDefaultOpacity = Math.Round(_pinOpacity.Value, 2);
            s.HidePinsFromCapture = _hidePins.IsChecked == true;
            s.PinTopmost = _pinTopmost.IsChecked == true;

            s.StartWithWindows = _startWithWindows.IsChecked == true;
            s.ShowTrayNotifications = _trayNotify.IsChecked == true;
            s.MinimizeToTrayOnly = _minimizeToTray.IsChecked == true;

            s.Normalize();
            return true;
        }

        // ---------------------------------------------------------------- 小工具

        private bool TryNumber(TextBox box, double min, double max, string name, out double value)
        {
            value = 0;
            string raw = (box.Text ?? string.Empty).Trim();
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                box.Focus();
                return Fail(name + " 需要是数字");
            }
            value = Math.Clamp(parsed, min, max);
            return true;
        }

        private static bool TryParseColor(string text, out Color color)
        {
            color = Colors.Transparent;
            try
            {
                if (ColorConverter.ConvertFromString(text) is Color parsed)
                {
                    color = parsed;
                    return true;
                }
            }
            catch (Exception)
            {
                // 非法颜色值，交给调用方提示
            }
            return false;
        }

        private void OnDefaultColorChanged(object sender, TextChangedEventArgs e)
        {
            SyncColorPreview();
        }

        private void SyncColorPreview()
        {
            if (_colorPreview == null || _defaultColor == null)
            {
                return;
            }
            _colorPreview.Background = TryParseColor((_defaultColor.Text ?? string.Empty).Trim(), out Color color)
                ? new SolidColorBrush(color)
                : Brushes.Transparent;
        }
    }
}