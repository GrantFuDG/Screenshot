using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenshotManager.Models;
using ScreenshotManager.Services;

namespace ScreenshotManager.Views
{
    /// <summary>
    /// 设置窗口：编辑 <see cref="AppSettings"/>，保存时交给 <see cref="SettingsService.Replace"/>
    /// 持久化并广播 <c>Changed</c>（外壳 + 通用控件工厂；各分组见 SettingsWindow.Sections.cs）。
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        internal static readonly Brush WindowBg = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x21));
        internal static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x2B));
        internal static readonly Brush FieldBg = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x16));
        internal static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEA));
        internal static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
        internal static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        internal static readonly Brush FieldBorder = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x46));
        internal static readonly FontFamily UiFont = new FontFamily("Microsoft YaHei UI, Segoe UI");

        private readonly SettingsService _service;
        private readonly StackPanel _body = new StackPanel();
        private readonly TextBlock _status = new TextBlock { Foreground = DimBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };

        public SettingsWindow(SettingsService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            Title = "设置";
            Width = 780;
            Height = 740;
            MinWidth = 660;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = WindowBg;
            Foreground = TextBrush;
            FontFamily = UiFont;
            Icon = IconFactory.CreateImageSource(32);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Border { Background = CardBg, Padding = new Thickness(16, 10, 16, 10) };
            header.Child = new TextBlock
            {
                Text = "设置",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 6, 16, 6)
            };
            scroll.Content = _body;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var footer = BuildFooter();
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;

            BuildSections();
            LoadFrom(_service.Settings);
        }

        // ---------------------------------------------------------------- 外壳

        private UIElement BuildFooter()
        {
            var bar = new Border { Background = CardBg, Padding = new Thickness(12, 8, 12, 8) };
            var dock = new DockPanel { LastChildFill = true };

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var save = MakeButton("保存并关闭", true);
            save.Click += OnSave;
            var cancel = MakeButton("取消");
            cancel.Click += (s, e) => Close();
            right.Children.Add(save);
            right.Children.Add(cancel);
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var reset = MakeButton("恢复默认");
            reset.Click += OnReset;
            var open = MakeButton("打开配置目录");
            open.Click += OnOpenConfigDir;
            var apply = MakeButton("应用");
            apply.Click += OnApply;
            left.Children.Add(reset);
            left.Children.Add(open);
            left.Children.Add(apply);
            left.Children.Add(_status);
            dock.Children.Add(left);

            bar.Child = dock;
            return bar;
        }

        private void AddSection(string title)
        {
            if (_body.Children.Count > 0)
            {
                _body.Children.Add(new Border { Height = 8 });
            }
            _body.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Accent,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 4)
            });
        }

        private void AddRow(string label, UIElement control, string hint = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var text = new TextBlock
            {
                Text = label,
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(control);
            if (!string.IsNullOrEmpty(hint))
            {
                host.Children.Add(new TextBlock
                {
                    Text = hint,
                    Foreground = DimBrush,
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            Grid.SetColumn(host, 1);
            grid.Children.Add(host);
            _body.Children.Add(grid);
        }

        private void AddNote(string text)
        {
            _body.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = DimBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 6)
            });
        }

        internal static TextBox MakeText(string value, double width = 240)
        {
            return new TextBox
            {
                Text = value ?? string.Empty,
                Width = width,
                Background = FieldBg,
                Foreground = TextBrush,
                BorderBrush = FieldBorder,
                CaretBrush = TextBrush,
                Padding = new Thickness(6, 3, 6, 3)
            };
        }

        internal static CheckBox MakeCheck(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = TextBrush,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        internal static Slider MakeSlider(double min, double max, double value, double tick)
        {
            return new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Width = 220,
                TickFrequency = tick,
                IsSnapToTickEnabled = tick > 0,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        internal static ComboBox MakeCombo(params string[] items)
        {
            var combo = new ComboBox { Width = 140, Background = FieldBg, Foreground = TextBrush };
            foreach (string item in items)
            {
                combo.Items.Add(item);
            }
            return combo;
        }

        private static Button MakeButton(string text, bool primary = false)
        {
            return new Button
            {
                Content = text,
                MinWidth = 84,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Background = primary ? Accent : FieldBorder,
                Foreground = primary ? Brushes.White : TextBrush,
                BorderThickness = new Thickness(0)
            };
        }

        // ---------------------------------------------------------------- 数值解析

        private bool TryInt(TextBox box, int min, int max, string name, out int value)
        {
            value = 0;
            if (!int.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                Fail(name + " 需要是整数");
                box.Focus();
                return false;
            }
            value = Math.Clamp(parsed, min, max);
            return true;
        }

        private bool Fail(string message)
        {
            _status.Text = "✗ " + message;
            return false;
        }

        // ---------------------------------------------------------------- 动作

        private bool Apply()
        {
            var fresh = _service.Settings.Clone();
            if (!ReadInto(fresh))
            {
                return false;
            }
            _service.Replace(fresh, true);
            _status.Text = "✓ 已保存 " + DateTime.Now.ToString("HH:mm:ss");
            return true;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            Apply();
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (Apply())
            {
                Close();
            }
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(this, "把所有设置恢复为默认值？", "截图管家",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
            _service.ResetToDefault();
            LoadFrom(_service.Settings);
            _status.Text = "✓ 已恢复默认";
        }

        private void OnOpenConfigDir(object sender, RoutedEventArgs e)
        {
            try
            {
                AppPaths.EnsureCreated();
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.RootDir + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Warn("打开配置目录失败: " + ex.Message);
                Fail("无法打开配置目录");
            }
        }

        private void OnBrowseSaveDir(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择截图保存目录", Multiselect = false };
            try
            {
                if (Directory.Exists(_saveDir.Text))
                {
                    dialog.InitialDirectory = _saveDir.Text;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("设置初始目录失败: " + ex.Message);
            }
            if (dialog.ShowDialog(this) == true)
            {
                _saveDir.Text = dialog.FolderName;
            }
        }
    }
}