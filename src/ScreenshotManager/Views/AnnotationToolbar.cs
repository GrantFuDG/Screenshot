using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ScreenshotManager.Models;

// WinForms/Drawing implicit usings make these names ambiguous; pin them to WPF.
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Cursor = System.Windows.Input.Cursor;
using Image = System.Windows.Controls.Image;
using Path = System.Windows.Shapes.Path;
using Panel = System.Windows.Controls.Panel;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace ScreenshotManager.Views
{
    /// <summary>The floating annotation toolbar shown next to the selection while capturing.</summary>
    public sealed class AnnotationToolbar : Border
    {
        private static readonly Color[] PaletteColors =
        {
            Color.FromRgb(0xE5, 0x39, 0x35), Color.FromRgb(0xD8, 0x1B, 0x60), Color.FromRgb(0x8E, 0x24, 0xAA),
            Color.FromRgb(0x5E, 0x35, 0xB1), Color.FromRgb(0x39, 0x49, 0xAB), Color.FromRgb(0x1E, 0x88, 0xE5),
            Color.FromRgb(0x03, 0x9B, 0xE5), Color.FromRgb(0x00, 0xAC, 0xC1), Color.FromRgb(0x00, 0x89, 0x7B),
            Color.FromRgb(0x43, 0xA0, 0x47), Color.FromRgb(0x7C, 0xB3, 0x42), Color.FromRgb(0xC0, 0xCA, 0x33),
            Color.FromRgb(0xFD, 0xD8, 0x35), Color.FromRgb(0xFF, 0xB3, 0x00), Color.FromRgb(0xFB, 0x8C, 0x00),
            Color.FromRgb(0xF4, 0x51, 0x1E), Color.FromRgb(0x6D, 0x4C, 0x41), Color.FromRgb(0x75, 0x75, 0x75),
            Color.FromRgb(0x54, 0x6E, 0x7A), Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF)
        };

        private static readonly Color Accent = Color.FromRgb(0x21, 0x96, 0xF3);
        private static readonly Brush IconBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF4));
        private static readonly Brush SeparatorBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

        private readonly AppSettings _settings;
        private readonly Dictionary<ToolKind, ToggleButton> _toolButtons = new Dictionary<ToolKind, ToggleButton>();
        private readonly Border _colorSwatch;
        private readonly Canvas _thicknessGlyph;
        private readonly Border _palettePanel;
        private readonly Border _thicknessPanel;
        private readonly StackPanel _root;
        private readonly Button _undoButton;
        private readonly Button _redoButton;
        private readonly Slider _thicknessSlider;
        private readonly TextBlock _thicknessLabel;

        private Color _color;
        private int _thickness;
        private double _uiScale = 1.0;
        private bool _ready;

        public ToolKind ActiveTool { get; private set; } = ToolKind.Rectangle;
        public Color ActiveColor => _color;
        public int ActiveThickness => _thickness;

        public event Action<ToolKind> ToolSelected;
        public event Action UndoRequested;
        public event Action RedoRequested;
        public event Action CopyRequested;
        public event Action SaveRequested;
        public event Action PinRequested;
        public event Action CancelRequested;
        public event Action ConfirmRequested;
        public event Action<Color> ColorChanged;
        public event Action<int> ThicknessChanged;

        public double ToolbarWidth => 620;

        public AnnotationToolbar(AppSettings settings)
        {
            _settings = settings ?? new AppSettings();
            _color = ParseColor(_settings.DefaultColor);
            _thickness = Math.Max(1, Math.Min(12, (int)Math.Round(_settings.DefaultThickness)));

            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x1D, 0x21));
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(6, 5, 6, 5);
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black };
            SnapsToDevicePixels = true;

            _root = new StackPanel { Orientation = Orientation.Vertical };
            Child = _root;

            _palettePanel = BuildPalettePanel();
            _palettePanel.Visibility = Visibility.Collapsed;
            _root.Children.Add(_palettePanel);

            Slider thicknessSlider;
            TextBlock thicknessLabel;
            _thicknessPanel = BuildThicknessPanel(out thicknessSlider, out thicknessLabel);
            _thicknessPanel.Visibility = Visibility.Collapsed;
            _root.Children.Add(_thicknessPanel);

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            AddTool(row, ToolKind.Rectangle, "rect", "矩形 (R)");
            AddTool(row, ToolKind.Ellipse, "ellipse", "椭圆 (E)");
            AddTool(row, ToolKind.Line, "line", "直线 (L)");
            AddTool(row, ToolKind.Arrow, "arrow", "箭头 (A)");
            AddTool(row, ToolKind.Pen, "pen", "画笔 (P)");
            AddTool(row, ToolKind.Highlighter, "marker", "记号笔 (H)");
            AddTool(row, ToolKind.Text, "text", "文字 (T)");
            AddTool(row, ToolKind.Mosaic, "mosaic", "马赛克 (M)");
            AddTool(row, ToolKind.Blur, "blur", "模糊 (B)");
            AddTool(row, ToolKind.Eraser, "eraser", "橡皮擦 (X)");

            row.Children.Add(Separator());

            _colorSwatch = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                Background = new SolidColorBrush(_color)
            };
            var colorButton = ChromeButton(_colorSwatch, "颜色", typeof(Button));
            colorButton.Click += (s, e) => TogglePalette();
            row.Children.Add(colorButton);

            _thicknessGlyph = new Canvas { Width = 18, Height = 18 };
            DrawThicknessGlyph();
            var thicknessButton = ChromeButton(_thicknessGlyph, "粗细", typeof(Button));
            thicknessButton.Click += (s, e) => ToggleThickness();
            row.Children.Add(thicknessButton);

            row.Children.Add(Separator());

            _undoButton = (Button)ChromeButton(ToolIcons.Create("undo", IconBrush), "撤销 (Ctrl+Z)", typeof(Button));
            _undoButton.Click += (s, e) => Fire(UndoRequested);
            _undoButton.IsEnabled = false;
            row.Children.Add(_undoButton);

            _redoButton = (Button)ChromeButton(ToolIcons.Create("redo", IconBrush), "重做 (Ctrl+Y)", typeof(Button));
            _redoButton.Click += (s, e) => Fire(RedoRequested);
            _redoButton.IsEnabled = false;
            row.Children.Add(_redoButton);

            row.Children.Add(Separator());

            var copyButton = ChromeButton(ToolIcons.Create("copy", IconBrush), "复制到剪贴板 (Ctrl+C)", typeof(Button));
            copyButton.Click += (s, e) => Fire(CopyRequested);
            row.Children.Add(copyButton);

            var saveButton = ChromeButton(ToolIcons.Create("save", IconBrush), "保存到文件 (Ctrl+S)", typeof(Button));
            saveButton.Click += (s, e) => Fire(SaveRequested);
            row.Children.Add(saveButton);

            var pinButton = ChromeButton(ToolIcons.Create("pin", IconBrush), "贴图到屏幕 (Ctrl+T)", typeof(Button));
            pinButton.Click += (s, e) => Fire(PinRequested);
            row.Children.Add(pinButton);

            row.Children.Add(Separator());

            var cancelButton = ChromeButton(ToolIcons.Create("cancel", new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80))), "取消 (Esc)", typeof(Button));
            cancelButton.Click += (s, e) => Fire(CancelRequested);
            row.Children.Add(cancelButton);

            var confirmButton = ChromeButton(ToolIcons.Create("check", new SolidColorBrush(Color.FromRgb(0x9C, 0xE8, 0x8F)), 2.0), "完成 (Enter)", typeof(Button));
            confirmButton.Click += (s, e) => Fire(ConfirmRequested);
            row.Children.Add(confirmButton);

            var mainRow = new Border
            {
                Background = Brushes.Transparent,
                Child = row
            };
            _root.Children.Add(mainRow);

            _thicknessSlider = thicknessSlider;
            _thicknessLabel = thicknessLabel;
            UpdateThicknessUi();
            _ready = true;

            SetActiveTool(ActiveTool);
            MouseDown += (s, e) => e.Handled = true;
        }

        /// <summary>Scales the toolbar so it keeps a constant apparent size on high-DPI monitors.</summary>
        public void SetUiScale(double scale)
        {
            if (scale <= 0.1) scale = 1.0;
            if (Math.Abs(scale - _uiScale) < 0.001) return;
            _uiScale = scale;
            LayoutTransform = new ScaleTransform(scale, scale);
        }

        public double ScaledWidth => ToolbarWidth * _uiScale;
        public double ScaledHeight => ActualHeight > 1 ? ActualHeight * _uiScale : 46 * _uiScale;

        public void SetActiveTool(ToolKind kind)
        {
            ActiveTool = kind;
            foreach (var pair in _toolButtons)
            {
                pair.Value.IsChecked = pair.Key == kind;
            }
        }

        public void SetColor(Color color)
        {
            _color = color;
            _colorSwatch.Background = new SolidColorBrush(color);
            var handler = ColorChanged;
            if (handler != null) handler(color);
        }

        public void SetThickness(int value)
        {
            _thickness = Math.Max(1, Math.Min(12, value));
            UpdateThicknessUi();
            var handler = ThicknessChanged;
            if (handler != null) handler(_thickness);
        }

        public void SetUndoRedo(bool canUndo, bool canRedo)
        {
            _undoButton.IsEnabled = canUndo;
            _redoButton.IsEnabled = canRedo;
        }

        public void HidePanels()
        {
            _palettePanel.Visibility = Visibility.Collapsed;
            _thicknessPanel.Visibility = Visibility.Collapsed;
        }

        private void TogglePalette()
        {
            bool show = _palettePanel.Visibility != Visibility.Visible;
            HidePanels();
            _palettePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleThickness()
        {
            bool show = _thicknessPanel.Visibility != Visibility.Visible;
            HidePanels();
            _thicknessPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) _thicknessSlider.Value = _thickness;
        }

        private void UpdateThicknessUi()
        {
            DrawThicknessGlyph();
            if (_thicknessSlider != null) _thicknessSlider.Value = _thickness;
            if (_thicknessLabel != null) _thicknessLabel.Text = _thickness + " px";
        }

        private void DrawThicknessGlyph()
        {
            if (_thicknessGlyph == null) return;
            _thicknessGlyph.Children.Clear();
            var line = new System.Windows.Shapes.Line
            {
                X1 = 2,
                Y1 = 9,
                X2 = 16,
                Y2 = 9,
                Stroke = IconBrush,
                StrokeThickness = Math.Max(1, Math.Min(7, _thickness)),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            _thicknessGlyph.Children.Add(line);
        }

        private void AddTool(Panel row, ToolKind kind, string icon, string tooltip)
        {
            var toggle = (ToggleButton)ChromeButton(ToolIcons.Create(icon, IconBrush), tooltip, typeof(ToggleButton));
            toggle.Click += (s, e) =>
            {
                if (ActiveTool != kind)
                {
                    SetActiveTool(kind);
                    var handler = ToolSelected;
                    if (handler != null) handler(kind);
                }
                else
                {
                    toggle.IsChecked = true;
                }
            };
            _toolButtons[kind] = toggle;
            row.Children.Add(toggle);
        }

        private Border BuildPalettePanel()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Width = 10 * 26 };
            foreach (var color in PaletteColors)
            {
                var swatch = new Border
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    Cursor = Cursors.Hand
                };
                var captured = color;
                swatch.MouseLeftButtonUp += (s, e) =>
                {
                    SetColor(captured);
                    _palettePanel.Visibility = Visibility.Collapsed;
                };
                wrap.Children.Add(swatch);
            }

            var custom = new Button
            {
                Content = new TextBlock { Text = "自定义…", FontSize = 12, Foreground = IconBrush },
                Width = 78,
                Height = 26,
                Margin = new Thickness(2, 6, 2, 0),
                Style = FlatStyle,
                Cursor = Cursors.Hand,
                Focusable = false
            };
            ApplyChrome(custom);
            custom.Click += (s, e) =>
            {
                using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B) })
                {
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        SetColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                        _palettePanel.Visibility = Visibility.Collapsed;
                    }
                }
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(wrap);
            stack.Children.Add(custom);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x1D, 0x21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 5),
                Child = stack
            };
        }

        private Border BuildThicknessPanel(out Slider outSlider, out TextBlock outLabel)
        {
            var slider = new Slider
            {
                Minimum = 1,
                Maximum = 12,
                Width = 220,
                VerticalAlignment = VerticalAlignment.Center,
                IsSnapToTickEnabled = true,
                TickFrequency = 1
            };
            slider.ValueChanged += (s, e) =>
            {
                if (!_ready) return;
                int v = (int)Math.Round(e.NewValue);
                if (v == _thickness) return;
                _thickness = v;
                UpdateThicknessUi();
                var handler = ThicknessChanged;
                if (handler != null) handler(v);
            };

            var label = new TextBlock
            {
                Text = "2 px",
                Foreground = IconBrush,
                FontSize = 12,
                Width = 40,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            outSlider = slider;
            outLabel = label;

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "粗细",
                Foreground = IconBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(slider);
            stack.Children.Add(label);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x1D, 0x21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Child = stack
            };
        }

        private static FrameworkElement Separator()
        {
            return new Border
            {
                Width = 1,
                Height = 20,
                Margin = new Thickness(6, 0, 6, 0),
                Background = SeparatorBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>Builds a flat icon button (or toggle) matching the dark toolbar theme.</summary>
        private static ButtonBase ChromeButton(FrameworkElement content, string tooltip, Type kind)
        {
            ButtonBase button;
            if (kind == typeof(ToggleButton)) button = new ToggleButton();
            else button = new Button();

            button.Content = content;
            button.ToolTip = tooltip;
            button.Width = 32;
            button.Height = 32;
            button.Margin = new Thickness(1, 0, 1, 0);
            button.Background = Brushes.Transparent;
            button.Foreground = IconBrush;
            button.Cursor = Cursors.Hand;
            button.Focusable = false;
            button.Style = FlatStyle;
            ApplyChrome(button);
            return button;
        }

        private static readonly Brush HoverBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        private static readonly Brush PressBrush = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        private static readonly Brush CheckBrush = Frozen(Accent);
        private static readonly Brush ClearBrush = Brushes.Transparent;

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static readonly Style FlatStyle = BuildFlatStyle();

        private static Style BuildFlatStyle()
        {
            var border = new FrameworkElementFactory(typeof(Border), "bg");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(ButtonBase)) { VisualTree = border };

            var style = new Style(typeof(ButtonBase));
            style.Setters.Add(new Setter(Control.BackgroundProperty, ClearBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, IconBrush));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        /// <summary>Mouse/checked state driven chrome; avoids template-trigger precedence pitfalls.</summary>
        private static void ApplyChrome(ButtonBase button)
        {
            bool hover = false, pressed = false;
            var toggle = button as ToggleButton;

            Action refresh = () =>
            {
                if (!button.IsEnabled)
                {
                    button.Background = ClearBrush;
                    button.Opacity = 0.35;
                    return;
                }
                button.Opacity = 1;
                if (toggle != null && toggle.IsChecked == true) button.Background = CheckBrush;
                else if (pressed) button.Background = PressBrush;
                else if (hover) button.Background = HoverBrush;
                else button.Background = ClearBrush;
            };

            button.MouseEnter += (s, e) => { hover = true; refresh(); };
            button.MouseLeave += (s, e) => { hover = false; pressed = false; refresh(); };
            button.PreviewMouseLeftButtonDown += (s, e) => { pressed = true; refresh(); };
            button.PreviewMouseLeftButtonUp += (s, e) => { pressed = false; refresh(); };
            button.IsEnabledChanged += (s, e) => refresh();
            if (toggle != null)
            {
                toggle.Checked += (s, e) => refresh();
                toggle.Unchecked += (s, e) => refresh();
            }
        }

        private static void Fire(Action handler)
        {
            var h = handler;
            if (h != null) h();
        }

        public static Color ParseColor(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Color.FromRgb(0xE5, 0x39, 0x35);
            try
            {
                var converted = ColorConverter.ConvertFromString(text.Trim());
                if (converted is Color color) return color;
            }
            catch
            {
            }
            return Color.FromRgb(0xE5, 0x39, 0x35);
        }

        public static string ColorToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", color.A, color.R, color.G, color.B);
        }
    }
}