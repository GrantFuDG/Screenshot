using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace ScreenshotManager.Models
{
    /// <summary>All persistent application paths.</summary>
    public static class AppPaths
    {
        private static string _rootDir;
        private static bool _portable;

        /// <summary>
        /// 配置目录：优先 %APPDATA%\ScreenshotManager；若该位置不可写（受限沙箱、只读漫游配置等），
        /// 退回程序目录下的 data\ （绿色模式），保证设置/历史/日志始终有落盘位置。
        /// </summary>
        public static string RootDir
        {
            get
            {
                if (_rootDir == null)
                {
                    string preferred = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenshotManager");
                    if (CanWrite(preferred))
                    {
                        _rootDir = preferred;
                    }
                    else
                    {
                        _rootDir = Path.Combine(AppContext.BaseDirectory, "data");
                        _portable = true;
                    }
                }
                return _rootDir;
            }
        }

        /// <summary>true 表示配置落在了程序目录（受限环境下无法写 %APPDATA%）。</summary>
        public static bool IsPortable => _portable;

        public static string SettingsFile => Path.Combine(RootDir, "settings.json");
        public static string HistoryFile => Path.Combine(RootDir, "history.json");
        public static string ThumbsDir => Path.Combine(RootDir, "thumbs");
        public static string LogFile => Path.Combine(RootDir, "screenshotmanager.log");

        /// <summary>配置目录内的截图目录，用作保存目录的兜底。</summary>
        public static string ScreenshotsDir => Path.Combine(RootDir, "Screenshots");

        /// <summary>优先「图片\Screenshots」，不可写时退回配置目录内的 Screenshots。</summary>
        public static string DefaultSaveDir
        {
            get
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (!string.IsNullOrEmpty(pictures))
                {
                    string preferred = Path.Combine(pictures, "Screenshots");
                    if (CanWrite(preferred))
                    {
                        return preferred;
                    }
                }
                return ScreenshotsDir;
            }
        }

        public static void EnsureCreated()
        {
            Directory.CreateDirectory(RootDir);
            Directory.CreateDirectory(ThumbsDir);
        }

        /// <summary>
        /// 真实可写性探测：CreateDirectory 对已存在的目录不会报错，必须实际写一个探针文件才能判断。
        /// </summary>
        public static bool CanWrite(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>User settings, persisted as JSON.</summary>
    public sealed class AppSettings
    {
        // hotkeys
        public string HotkeyCapture { get; set; } = "F1";
        public string HotkeyCaptureFullScreen { get; set; } = "Ctrl+F1";
        public string HotkeyPaste { get; set; } = "F3";
        public string HotkeyHistory { get; set; } = "Ctrl+Shift+H";
        public string HotkeyPinClipboard { get; set; } = "Shift+F3";

        // saving
        public string SaveDirectory { get; set; } = AppPaths.DefaultSaveDir;
        public string FileNameTemplate { get; set; } = "Shot_{yyyyMMdd_HHmmss}";
        public string ImageFormat { get; set; } = "png";         // png | jpg | bmp
        public int JpegQuality { get; set; } = 92;
        public bool AutoSaveOnCopy { get; set; } = true;
        public bool AutoCopyToClipboard { get; set; } = true;
        public bool PinAfterCapture { get; set; } = false;

        // history
        public int HistoryMaxCount { get; set; } = 300;
        public int HistoryMaxAgeDays { get; set; } = 0;          // 0 = never expire

        // capture behaviour
        public bool ShowMagnifier { get; set; } = true;
        public int MagnifierZoom { get; set; } = 4;
        public bool MagnifierShowColor { get; set; } = true;
        public bool DetectWindowOnHover { get; set; } = true;
        public bool ShowSelectionSize { get; set; } = true;

        // annotation defaults
        public string DefaultColor { get; set; } = "#FFE53935";
        public double DefaultThickness { get; set; } = 2;
        public bool MosaicBlurMode { get; set; } = false;         // false = mosaic, true = blur
        public int MosaicBlockSize { get; set; } = 12;

        // pins
        public double PinDefaultOpacity { get; set; } = 1.0;
        public bool HidePinsFromCapture { get; set; } = true;
        public bool PinTopmost { get; set; } = true;

        // shell
        public bool StartWithWindows { get; set; } = false;
        public bool ShowTrayNotifications { get; set; } = true;
        public bool MinimizeToTrayOnly { get; set; } = true;

        public static AppSettings CreateDefault() => new AppSettings();

        public AppSettings Clone()
        {
            return new AppSettings
            {
                HotkeyCapture = HotkeyCapture,
                HotkeyCaptureFullScreen = HotkeyCaptureFullScreen,
                HotkeyPaste = HotkeyPaste,
                HotkeyHistory = HotkeyHistory,
                HotkeyPinClipboard = HotkeyPinClipboard,
                SaveDirectory = SaveDirectory,
                FileNameTemplate = FileNameTemplate,
                ImageFormat = ImageFormat,
                JpegQuality = JpegQuality,
                AutoSaveOnCopy = AutoSaveOnCopy,
                AutoCopyToClipboard = AutoCopyToClipboard,
                PinAfterCapture = PinAfterCapture,
                HistoryMaxCount = HistoryMaxCount,
                HistoryMaxAgeDays = HistoryMaxAgeDays,
                ShowMagnifier = ShowMagnifier,
                MagnifierZoom = MagnifierZoom,
                MagnifierShowColor = MagnifierShowColor,
                DetectWindowOnHover = DetectWindowOnHover,
                ShowSelectionSize = ShowSelectionSize,
                DefaultColor = DefaultColor,
                DefaultThickness = DefaultThickness,
                MosaicBlurMode = MosaicBlurMode,
                MosaicBlockSize = MosaicBlockSize,
                PinDefaultOpacity = PinDefaultOpacity,
                HidePinsFromCapture = HidePinsFromCapture,
                PinTopmost = PinTopmost,
                StartWithWindows = StartWithWindows,
                ShowTrayNotifications = ShowTrayNotifications,
                MinimizeToTrayOnly = MinimizeToTrayOnly
            };
        }

        /// <summary>Keeps the loaded file inside sane ranges.</summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(SaveDirectory)) SaveDirectory = AppPaths.DefaultSaveDir;
            if (string.IsNullOrWhiteSpace(FileNameTemplate)) FileNameTemplate = "Shot_{yyyyMMdd_HHmmss}";
            ImageFormat = (ImageFormat ?? "png").Trim().ToLowerInvariant();
            if (ImageFormat != "png" && ImageFormat != "jpg" && ImageFormat != "bmp") ImageFormat = "png";
            JpegQuality = Math.Clamp(JpegQuality, 10, 100);
            HistoryMaxCount = Math.Clamp(HistoryMaxCount, 10, 100000);
            HistoryMaxAgeDays = Math.Clamp(HistoryMaxAgeDays, 0, 3650);
            MagnifierZoom = Math.Clamp(MagnifierZoom, 1, 16);
            MosaicBlockSize = Math.Clamp(MosaicBlockSize, 2, 64);
            DefaultThickness = Math.Clamp(DefaultThickness, 1, 40);
            PinDefaultOpacity = Math.Clamp(PinDefaultOpacity, 0.1, 1.0);
            if (string.IsNullOrWhiteSpace(DefaultColor)) DefaultColor = "#FFE53935";
            if (string.IsNullOrWhiteSpace(HotkeyCapture)) HotkeyCapture = "F1";
        }
    }

    public static class Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }
}