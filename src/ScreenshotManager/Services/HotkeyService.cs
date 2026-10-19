using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenshotManager.Interop;
using ScreenshotManager.Services;

namespace ScreenshotManager.Services
{
    /// <summary>A parsed global hotkey such as "Ctrl+Shift+H".</summary>
    public sealed class HotkeyGesture
    {
        public uint Modifiers { get; private set; }
        public uint VirtualKey { get; private set; }
        public string KeyName { get; private set; } = "";

        public bool IsValid => VirtualKey != 0;

        private static readonly Dictionary<string, uint> Named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "PrintScreen", 0x2C }, { "PrtSc", 0x2C }, { "ScrollLock", 0x91 }, { "Pause", 0x13 },
            { "Insert", 0x2D }, { "Ins", 0x2D }, { "Delete", 0x2E }, { "Del", 0x2E },
            { "Home", 0x24 }, { "End", 0x23 }, { "PageUp", 0x21 }, { "PageDown", 0x22 },
            { "Up", 0x26 }, { "Down", 0x28 }, { "Left", 0x25 }, { "Right", 0x27 },
            { "Space", 0x20 }, { "Enter", 0x0D }, { "Return", 0x0D }, { "Tab", 0x09 },
            { "Escape", 0x1B }, { "Esc", 0x1B }, { "Backspace", 0x08 }, { "Back", 0x08 },
            { "OemMinus", 0xBD }, { "OemPlus", 0xBB }, { "OemComma", 0xBC }, { "OemPeriod", 0xBE },
            { "OemQuestion", 0xBF }, { "OemTilde", 0xC0 }, { "OemOpenBrackets", 0xDB },
            { "OemCloseBrackets", 0xDD }, { "OemPipe", 0xDC }, { "OemSemicolon", 0xBA }, { "OemQuotes", 0xDE },
            { "NumPad0", 0x60 }, { "NumPad1", 0x61 }, { "NumPad2", 0x62 }, { "NumPad3", 0x63 },
            { "NumPad4", 0x64 }, { "NumPad5", 0x65 }, { "NumPad6", 0x66 }, { "NumPad7", 0x67 },
            { "NumPad8", 0x68 }, { "NumPad9", 0x69 }
        };

        public static HotkeyGesture Parse(string text)
        {
            var g = new HotkeyGesture();
            if (string.IsNullOrWhiteSpace(text)) return g;

            string key = null;
            foreach (var rawPart in text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = rawPart.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        g.Modifiers |= Native.MOD_CONTROL; continue;
                    case "alt":
                        g.Modifiers |= Native.MOD_ALT; continue;
                    case "shift":
                        g.Modifiers |= Native.MOD_SHIFT; continue;
                    case "win":
                    case "meta":
                    case "super":
                        g.Modifiers |= Native.MOD_WIN; continue;
                    default:
                        key = part; continue;
                }
            }

            if (key == null) return g;
            uint vk = KeyNameToVirtualKey(key);
            if (vk == 0) return g;
            g.VirtualKey = vk;
            g.KeyName = CanonicalKeyName(vk, key);
            return g;
        }

        private static uint KeyNameToVirtualKey(string key)
        {
            if (key.Length == 1)
            {
                char c = char.ToUpperInvariant(key[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
                switch (c)
                {
                    case '`': return 0xC0;
                    case '-': return 0xBD;
                    case '=': return 0xBB;
                    case '[': return 0xDB;
                    case ']': return 0xDD;
                    case '\\': return 0xDC;
                    case ';': return 0xBA;
                    case '\'': return 0xDE;
                    case ',': return 0xBC;
                    case '.': return 0xBE;
                    case '/': return 0xBF;
                }
            }
            if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f'))
            {
                int n;
                if (int.TryParse(key.Substring(1), out n) && n >= 1 && n <= 24) return (uint)(0x70 + n - 1);
            }
            uint named;
            if (Named.TryGetValue(key, out named)) return named;
            return 0;
        }

        private static string CanonicalKeyName(uint vk, string fallback)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
            foreach (var kv in Named)
            {
                if (kv.Value == vk && kv.Key.Length > 1) return kv.Key;
            }
            return fallback;
        }

        /// <summary>Formats a WPF key + modifiers into the canonical gesture string.</summary>
        public static string Format(Key key, ModifierKeys modifiers)
        {
            uint vk = KeyToVirtualKey(key);
            if (vk == 0) return null;
            string name;
            if (vk >= 0x70 && vk <= 0x87) name = "F" + (vk - 0x70 + 1);
            else if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) name = ((char)vk).ToString();
            else name = CanonicalKeyName(vk, key.ToString());

            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(name);
            return string.Join("+", parts);
        }

        public static uint KeyToVirtualKey(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return (uint)('A' + (key - Key.A));
            if (key >= Key.D0 && key <= Key.D9) return (uint)('0' + (key - Key.D0));
            if (key >= Key.F1 && key <= Key.F24) return (uint)(0x70 + (key - Key.F1));
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return (uint)(0x60 + (key - Key.NumPad0));
            switch (key)
            {
                case Key.PrintScreen: return 0x2C;
                case Key.Scroll: return 0x91;
                case Key.Pause: return 0x13;
                case Key.Insert: return 0x2D;
                case Key.Delete: return 0x2E;
                case Key.Home: return 0x24;
                case Key.End: return 0x23;
                case Key.PageUp: return 0x21;
                case Key.PageDown: return 0x22;
                case Key.Up: return 0x26;
                case Key.Down: return 0x28;
                case Key.Left: return 0x25;
                case Key.Right: return 0x27;
                case Key.Space: return 0x20;
                case Key.Enter: return 0x0D;
                case Key.Tab: return 0x09;
                case Key.Escape: return 0x1B;
                case Key.Back: return 0x08;
                case Key.OemMinus: return 0xBD;
                case Key.OemPlus: return 0xBB;
                case Key.OemComma: return 0xBC;
                case Key.OemPeriod: return 0xBE;
                case Key.OemQuestion: return 0xBF;
                case Key.OemTilde: return 0xC0;
                case Key.OemOpenBrackets: return 0xDB;
                case Key.OemCloseBrackets: return 0xDD;
                case Key.OemPipe: return 0xDC;
                case Key.OemSemicolon: return 0xBA;
                case Key.OemQuotes: return 0xDE;
            }
            return 0;
        }

        public override string ToString()
        {
            if (!IsValid) return "";
            var parts = new List<string>();
            if ((Modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((Modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(KeyName);
            return string.Join("+", parts);
        }
    }

    /// <summary>Registers system-wide hotkeys on a hidden message window.</summary>
    public sealed class HotkeyService : IDisposable
    {
        private sealed class MessageWindow : Window
        {
            public event Action<int> HotkeyPressed;

            public MessageWindow()
            {
                WindowStyle = WindowStyle.None;
                ShowInTaskbar = false;
                ResizeMode = ResizeMode.NoResize;
                Width = 1;
                Height = 1;
                Left = -32000;
                Top = -32000;
                ShowActivated = false;
                Title = "ScreenshotManager.HotkeyWindow";
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                var source = PresentationSource.FromVisual(this) as HwndSource;
                source?.AddHook(WndProc);
            }

            private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (msg == Native.WM_HOTKEY)
                {
                    int id = wParam.ToInt32();
                    int lp = lParam.ToInt32();
                    AppLog.Info("收到 WM_HOTKEY id=0x" + id.ToString("X4")
                        + " mods=0x" + (lp & 0xFFFF).ToString("X4")
                        + " vk=0x" + ((lp >> 16) & 0xFFFF).ToString("X2"));
                    handled = true;
                    HotkeyPressed?.Invoke(id);
                }
                return IntPtr.Zero;
            }
        }

        private sealed class Registration
        {
            public string Action;
            public HotkeyGesture Gesture;
            public int Id;
        }

        private readonly MessageWindow _window = new MessageWindow();
        private readonly Dictionary<string, Registration> _byAction = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Registration> _byId = new Dictionary<int, Registration>();
        private int _nextId = 0x5100;
        private bool _attached;

        /// <summary>Raised with the action name registered by <see cref="Register"/>.</summary>
        public event Action<string> Pressed;

        public bool IsAttached => _attached;

        public void Attach()
        {
            if (_attached) return;
            _window.HotkeyPressed += OnHotkeyMessage;
            _window.Show();
            _attached = true;
            AppLog.Info("热键消息窗口已创建");
        }

        private void OnHotkeyMessage(int id)
        {
            Registration reg;
            if (_byId.TryGetValue(id, out reg))
            {
                Pressed?.Invoke(reg.Action);
            }
        }

        public bool Register(string action, string gestureText, out string error)
        {
            error = null;
            Attach();
            Unregister(action);

            var gesture = HotkeyGesture.Parse(gestureText);
            if (!gesture.IsValid)
            {
                error = $"无法识别的热键：{gestureText}";
                return false;
            }

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                error = "热键窗口尚未就绪";
                return false;
            }

            int id = _nextId++;
            if (!Native.RegisterHotKey(hwnd, id, gesture.Modifiers | Native.MOD_NOREPEAT, gesture.VirtualKey))
            {
                int code = Marshal.GetLastWin32Error();
                error = code == 1409 ? $"热键已被其他程序占用：{gesture}" : $"注册热键失败：{gesture} (错误 {code})";
                return false;
            }

            var reg = new Registration { Action = action, Gesture = gesture, Id = id };
            _byAction[action] = reg;
            _byId[id] = reg;
            AppLog.Info($"热键已注册 {action} = {gesture}");
            return true;
        }

        public void Unregister(string action)
        {
            Registration reg;
            if (!_byAction.TryGetValue(action, out reg)) return;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero) Native.UnregisterHotKey(hwnd, reg.Id);
            _byAction.Remove(action);
            _byId.Remove(reg.Id);
        }

        public void UnregisterAll()
        {
            foreach (var action in new List<string>(_byAction.Keys)) Unregister(action);
        }

        public void Dispose()
        {
            try
            {
                UnregisterAll();
                _window.HotkeyPressed -= OnHotkeyMessage;
                _window.Close();
            }
            catch (Exception ex)
            {
                AppLog.Warn("释放热键服务时出错: " + ex.Message);
            }
            _attached = false;
        }
    }
}