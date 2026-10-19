using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// WinForms/Drawing implicit usings make these names ambiguous; pin them to WPF.
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;
using Path = System.Windows.Shapes.Path;

namespace ScreenshotManager.Views
{
    /// <summary>Small vector icons drawn in a 24x24 design space, so the UI needs no image assets.</summary>
    internal static class ToolIcons
    {
        public static FrameworkElement Create(string key, Brush stroke, double thickness = 1.7)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            foreach (var path in Build(key, stroke, thickness)) canvas.Children.Add(path);
            return new Viewbox { Width = 18, Height = 18, Child = canvas, Stretch = Stretch.Uniform };
        }

        private static Path P(string data, Brush stroke, double thickness, Brush fill = null)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke,
                StrokeThickness = thickness,
                Fill = fill,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeDashCap = PenLineCap.Round
            };
        }

        private static System.Collections.Generic.List<Path> Build(string key, Brush stroke, double t)
        {
            var list = new System.Collections.Generic.List<Path>();
            switch (key)
            {
                case "rect":
                    list.Add(P("M3.5,5.5 H20.5 V18.5 H3.5 Z", stroke, t));
                    break;
                case "ellipse":
                    list.Add(new Path
                    {
                        Data = new EllipseGeometry(new Point(12, 12), 8.5, 6.5),
                        Stroke = stroke,
                        StrokeThickness = t
                    });
                    break;
                case "line":
                    list.Add(P("M4,20 L20,4", stroke, t));
                    break;
                case "arrow":
                    list.Add(P("M4,20 L19,5", stroke, t));
                    list.Add(P("M11.5,4.5 H19.5 V12.5", stroke, t));
                    break;
                case "pen":
                    list.Add(P("M4.5,19.5 L8.5,18 L18.5,8 L16,5.5 L6,15.5 Z", stroke, t));
                    break;
                case "marker":
                    list.Add(P("M5,13 L11,7 L16.5,12.5 L10.5,18.5 Z", stroke, t, stroke));
                    list.Add(P("M3.5,20.5 H20.5", stroke, t + 1.4));
                    break;
                case "mosaic":
                    list.Add(P("M4,4 H10.6 V10.6 H4 Z M13.4,4 H20 V10.6 H13.4 Z M4,13.4 H10.6 V20 H4 Z M13.4,13.4 H20 V20 H13.4 Z", stroke, 1.1, stroke));
                    break;
                case "blur":
                    list.Add(new Path
                    {
                        Data = new EllipseGeometry(new Point(12, 12), 8.5, 7.5),
                        Fill = stroke,
                        Opacity = 0.55
                    });
                    list.Add(new Path
                    {
                        Data = new EllipseGeometry(new Point(12, 12), 4.6, 4.0),
                        Fill = stroke
                    });
                    break;
                case "eraser":
                    list.Add(P("M6,15 L13,8 L19.5,14.5 L14,20 H9.5 Z", stroke, t));
                    list.Add(P("M3.5,21.5 H20.5", stroke, t));
                    break;
                case "text":
                    list.Add(P("M6,5 H18 M12,5 V19 M9.5,19 H14.5", stroke, t));
                    break;
                case "undo":
                    list.Add(P("M9,6.5 L4.5,11 L9,15.5", stroke, t));
                    list.Add(P("M4.5,11 H14.5 A5,5 0 0 1 14.5,21 H9", stroke, t));
                    break;
                case "redo":
                    list.Add(P("M15,6.5 L19.5,11 L15,15.5", stroke, t));
                    list.Add(P("M19.5,11 H9.5 A5,5 0 0 0 9.5,21 H15", stroke, t));
                    break;
                case "copy":
                    list.Add(P("M8.5,3.5 H18.5 V15.5 H8.5 Z", stroke, t));
                    list.Add(P("M5.5,8.5 H3.5 V20.5 H13.5 V18.5", stroke, t));
                    break;
                case "save":
                    list.Add(P("M3.5,3.5 H17 L20.5,7 V20.5 H3.5 Z", stroke, t));
                    list.Add(P("M7.5,3.5 V10.5 H16.5 V3.5", stroke, t));
                    list.Add(P("M6.5,20.5 V14 H17.5 V20.5", stroke, t));
                    break;
                case "pin":
                    list.Add(P("M8.5,4 H15.5 L14.2,11 L17.5,14.5 H6.5 L10,11 Z", stroke, t));
                    list.Add(P("M12,14.5 V20.5", stroke, t));
                    break;
                case "cancel":
                    list.Add(P("M6,6 L18,18 M18,6 L6,18", stroke, t));
                    break;
                case "check":
                    list.Add(P("M4.5,12.5 L9.5,18 L19.5,5.5", stroke, t + 0.5));
                    break;
                case "ocr":
                    list.Add(P("M4,8 V4 H8 M16,4 H20 V8 M20,16 V20 H16 M8,20 H4 V16", stroke, t));
                    list.Add(P("M8,15 V9 H13.2 A2.4,2.4 0 0 1 13.2,15 Z", stroke, t * 0.8));
                    break;
                case "scale":
                    list.Add(P("M4,8 H20 M4,12 H20 M4,16 H20", stroke, t));
                    break;
                default:
                    list.Add(P("M5,5 H19 V19 H5 Z", stroke, t));
                    break;
            }
            return list;
        }
    }
}