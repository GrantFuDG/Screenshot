using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotManager.Models;
using ScreenshotManager.Services;

namespace ScreenshotManager.Views
{
    /// <summary>截图历史浏览器：缩略图墙 + 搜索 + 复制/贴图/另存/删除。</summary>
    public sealed class HistoryWindow : Window
    {
        private static readonly Brush WindowBg = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x21));
        private static readonly Brush ToolbarBg = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x2B));
        private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x34));
        private static readonly Brush CardImgBg = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x16));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEA));
        private static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        private static readonly Brush IdleBtn = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x3F));
        private static readonly FontFamily UiFont = new FontFamily("Microsoft YaHei UI, Segoe UI");

        private readonly HistoryService _history;
        private readonly AppSettings _settings;
        private readonly WrapPanel _panel = new WrapPanel();
        private readonly TextBox _search = new TextBox();
        private readonly TextBlock _status = new TextBlock();
        private readonly List<Border> _cards = new List<Border>();

        private ScreenshotItem _selected;

        public event Action<ScreenshotItem> CopyRequested;
        public event Action<ScreenshotItem> PinRequested;

        public HistoryWindow(HistoryService history, AppSettings settings)
        {
            _history = history;
            _settings = settings;

            Title = "截图历史";
            Width = 940;
            Height = 640;
            MinWidth = 620;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = WindowBg;
            Icon = IconFactory.CreateImageSource(32);
            FontFamily = UiFont;
            Foreground = TextBrush;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(BuildToolbar());

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(8)
            };
            scroll.Content = _panel;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            _status.Foreground = DimBrush;
            _status.FontSize = 12;
            _status.Margin = new Thickness(12, 6, 12, 8);
            Grid.SetRow(_status, 2);
            root.Children.Add(_status);

            Content = root;
            PreviewKeyDown += OnPreviewKeyDown;
            _history.Changed += OnHistoryChanged;
            Closed += (s, e) => _history.Changed -= OnHistoryChanged;
            Loaded += (s, e) => Refresh();
        }

        private UIElement BuildToolbar()
        {
            var bar = new Border { Background = ToolbarBg, Padding = new Thickness(10, 8, 10, 8) };
            var dock = new DockPanel { LastChildFill = true };

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(MakeButton("复制", () => RaiseCopy()));
            right.Children.Add(MakeButton("贴图到屏幕", () => RaisePin()));
            right.Children.Add(MakeButton("另存为…", SaveAsSelected));
            right.Children.Add(MakeButton("打开", OpenSelected));
            right.Children.Add(MakeButton("删除", DeleteSelected));
            right.Children.Add(MakeButton("清空历史", ClearAll));
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);

            _search.Width = 240;
            _search.Height = 26;
            _search.VerticalContentAlignment = VerticalAlignment.Center;
            _search.Background = CardBg;
            _search.Foreground = TextBrush;
            _search.BorderBrush = IdleBtn;
            _search.CaretBrush = TextBrush;
            _search.Padding = new Thickness(6, 0, 6, 0);
            _search.ToolTip = "按文件名搜索";
            _search.TextChanged += (s, e) => Refresh();

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(new TextBlock
            {
                Text = "搜索",
                Foreground = DimBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            left.Children.Add(_search);
            left.Children.Add(MakeButton("打开保存目录", OpenSaveFolder));
            dock.Children.Add(left);

            bar.Child = dock;
            return bar;
        }

        // ------------------------------------------------------------------ data

        private void OnHistoryChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Refresh)); return; }
            Refresh();
        }

        public void Refresh()
        {
            string filter = (_search.Text ?? "").Trim();
            _panel.Children.Clear();
            _cards.Clear();
            _selected = null;

            List<ScreenshotItem> items = _history.Items.ToList();
            items = items.OrderByDescending(i => i.CreatedAt).ToList();
            if (filter.Length > 0)
            {
                items = items.Where(i =>
                    (i.FileName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (i.FilePath ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            long total = 0;
            foreach (var item in items)
            {
                total += item.SizeBytes;
                _panel.Children.Add(CreateCard(item));
            }

            long allTotal = _history.Items.Sum(i => i.SizeBytes);
            _status.Text = items.Count == _history.Count
                ? $"共 {items.Count} 张 · {FormatSize(allTotal)} · 保存目录 {_settings.SaveDirectory}"
                : $"筛选出 {items.Count} / {_history.Count} 张 · {FormatSize(total)}";

            if (items.Count == 0)
            {
                _panel.Children.Add(new TextBlock
                {
                    Text = _history.Count == 0 ? "还没有截图。按 F1 开始第一次截图。" : "没有匹配的截图。",
                    Foreground = DimBrush,
                    Margin = new Thickness(16)
                });
            }
        }

        private Border CreateCard(ScreenshotItem item)
        {
            var card = new Border
            {
                Width = 212,
                Margin = new Thickness(6),
                CornerRadius = new CornerRadius(7),
                Background = CardBg,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = item.FilePath + "\n" + item.DisplaySize + " · " + item.DisplayDate
            };

            var stack = new StackPanel();

            var host = new Border
            {
                Height = 132,
                Background = CardImgBg,
                CornerRadius = new CornerRadius(5, 5, 0, 0)
            };
            var src = LoadImage(item.ThumbPath, item.FilePath);
            if (src != null)
            {
                host.Child = new Image
                {
                    Source = src,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(4),
                    SnapsToDevicePixels = true
                };
            }
            else
            {
                host.Child = new TextBlock
                {
                    Text = "文件已丢失",
                    Foreground = DimBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            stack.Children.Add(host);

            var name = new TextBlock
            {
                Text = string.IsNullOrEmpty(item.FileName) ? item.Id : item.FileName,
                Foreground = TextBrush,
                FontSize = 12,
                Margin = new Thickness(8, 6, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(name);

            var meta = new TextBlock
            {
                Text = item.DisplaySize + " · " + item.DisplayDate,
                Foreground = DimBrush,
                FontSize = 11,
                Margin = new Thickness(8, 2, 8, 8),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(meta);

            card.Child = stack;
            card.Tag = item;
            card.MouseLeftButtonDown += (s, e) => Select(item, card);
            card.MouseRightButtonUp += (s, e) =>
            {
                Select(item, card);
                var menu = new ContextMenu();
                menu.Items.Add(MenuItemFor("复制到剪贴板", () => RaiseCopy()));
                menu.Items.Add(MenuItemFor("贴图到屏幕", () => RaisePin()));
                menu.Items.Add(MenuItemFor("另存为…", SaveAsSelected));
                menu.Items.Add(MenuItemFor("在资源管理器中显示", RevealSelected));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItemFor("删除…", DeleteSelected));
                card.ContextMenu = menu;
                menu.IsOpen = true;
                e.Handled = true;
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount >= 2) OpenSelected();
            };

            _cards.Add(card);
            return card;
        }

        private static MenuItem MenuItemFor(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (s, e) => action();
            return mi;
        }

        private void Select(ScreenshotItem item, Border card)
        {
            _selected = item;
            foreach (var c in _cards)
            {
                bool on = ReferenceEquals(c, card);
                c.BorderBrush = on ? Accent : Brushes.Transparent;
                c.Background = on ? new SolidColorBrush(Color.FromRgb(0x32, 0x38, 0x40)) : CardBg;
            }
        }

        // ------------------------------------------------------------------ actions

        private bool RequireSelection()
        {
            if (_selected != null && File.Exists(_selected.FilePath)) return true;
            MessageBox.Show(this, "请先选择一张截图。", "截图历史", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void RaiseCopy()
        {
            if (!RequireSelection()) return;
            var h = CopyRequested;
            if (h != null) h(_selected);
        }

        private void RaisePin()
        {
            if (!RequireSelection()) return;
            var h = PinRequested;
            if (h != null) h(_selected);
        }

        private void SaveAsSelected()
        {
            if (!RequireSelection()) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "另存为",
                FileName = _selected.FileName,
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp|所有文件|*.*",
                InitialDirectory = Directory.Exists(_settings.SaveDirectory) ? _settings.SaveDirectory : AppPaths.DefaultSaveDir
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.Copy(_selected.FilePath, dlg.FileName, true);
                _status.Text = "已保存到 " + dlg.FileName;
            }
            catch (Exception ex)
            {
                AppLog.Error("另存为失败", ex);
                MessageBox.Show(this, "保存失败：" + ex.Message, "截图历史", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenSelected()
        {
            if (!RequireSelection()) return;
            OpenPath(_selected.FilePath);
        }

        private void RevealSelected()
        {
            if (!RequireSelection()) return;
            RevealPath(_selected.FilePath);
        }

        private void DeleteSelected()
        {
            if (!RequireSelection()) return;
            if (MessageBox.Show(this, "确定删除选中的截图吗？文件也会被删除。", "截图历史",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var item = _selected;
            _selected = null;
            _history.Remove(item, true);
            Refresh();
        }

        private void ClearAll()
        {
            if (_history.Count == 0) return;
            if (MessageBox.Show(this, "确定清空全部截图历史吗？所有文件都会被删除。", "截图历史",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _selected = null;
            _history.Clear(true);
            Refresh();
        }

        private void OpenSaveFolder()
        {
            string dir = _settings.SaveDirectory;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = AppPaths.DefaultSaveDir;
            AppPaths.EnsureCreated();
            if (!Directory.Exists(dir)) dir = AppPaths.RootDir;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Error("打开保存目录失败", ex); }
        }

        private static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Error("打开文件失败", ex); }
        }

        private static void RevealPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { AppLog.Error("定位文件失败", ex); }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (e.Key == Key.F5) { Refresh(); e.Handled = true; return; }
            if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.C) { RaiseCopy(); e.Handled = true; }
                else if (e.Key == Key.S) { SaveAsSelected(); e.Handled = true; }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static BitmapSource LoadImage(string thumbPath, string fullPath)
        {
            try
            {
                string path = null;
                if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath)) path = thumbPath;
                else if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath)) path = fullPath;
                if (path == null) return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 220;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                AppLog.Warn("缩略图加载失败: " + thumbPath, ex);
                return null;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MB";
        }

        private Button MakeButton(string text, Action action)
        {
            var button = new Button
            {
                Content = text,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(10, 0, 10, 0),
                Background = IdleBtn,
                Foreground = TextBrush,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false,
                FontFamily = UiFont,
                FontSize = 12
            };
            button.Click += (s, e) => action();
            button.MouseEnter += (s, e) => button.Background = Accent;
            button.MouseLeave += (s, e) => button.Background = IdleBtn;
            return button;
        }
    }
}