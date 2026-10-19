using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotManager.Models;
using ScreenshotManager.Interop;
using ScreenshotManager.Services;
using Gdi = System.Drawing;

// WinForms/Drawing implicit usings make these names ambiguous; pin them to WPF.
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Cursor = System.Windows.Input.Cursor;
using Image = System.Windows.Controls.Image;

namespace ScreenshotManager.Views
{
    /// <summary>Which interaction phase the capture is in.</summary>
    public enum CapturePhase
    {
        Hovering,
        Selecting,
        Annotating
    }

    /// <summary>
    /// Shared state of one capture session. Every overlay window (one per monitor) reads and mutates
    /// this object, so a selection can span monitors. All coordinates are global physical pixels.
    /// </summary>
    public sealed class CaptureSession
    {
        public ScreenSnapshot Snapshot { get; }
        public AppSettings Settings { get; }
        public Gdi.Rectangle VirtualBounds => Snapshot.Bounds;

        public List<Annotation> Annotations { get; } = new List<Annotation>();
        private readonly Stack<Annotation> _redo = new Stack<Annotation>();

        public CapturePhase Phase { get; set; } = CapturePhase.Hovering;
        public Rect Selection { get; set; }
        public bool HasSelection { get; set; }
        public bool HasHoverWindow { get; set; }
        public Rect HoverWindow { get; set; }
        public Point Cursor { get; set; }
        public Point CursorGlobal { get; set; }
        /// <summary>Annotation currently being drawn (shared so every overlay can render it).</summary>
        public Annotation InProgress { get; set; }
        /// <summary>Text annotation being typed, if any.</summary>
        public TextAnnotation EditingText { get; set; }

        public ToolKind Tool { get; set; } = ToolKind.Rectangle;
        public Color Color { get; set; }
        public int Thickness { get; set; }
        public bool MosaicBlurMode { get; set; }

        /// <summary>Raised whenever the shared state changes so every overlay repaints.</summary>
        public event Action Changed;

        public event Action CopyRequested;
        public event Action SaveRequested;
        public event Action PinRequested;
        public event Action ConfirmRequested;
        public event Action CancelRequested;

        public CaptureSession(ScreenSnapshot snapshot, AppSettings settings)
        {
            Snapshot = snapshot;
            Settings = settings ?? new AppSettings();
            Color = ParseColor(Settings.DefaultColor);
            Thickness = Math.Max(1, Math.Min(12, (int)Math.Round(Settings.DefaultThickness)));
            MosaicBlurMode = Settings.MosaicBlurMode;
            Tool = ToolKind.Rectangle;
        }

        public static Color ParseColor(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return System.Windows.Media.Color.FromRgb(0xE5, 0x39, 0x35);
            try
            {
                var value = ColorConverter.ConvertFromString(text.Trim());
                if (value is Color c) return c;
            }
            catch
            {
            }
            return System.Windows.Media.Color.FromRgb(0xE5, 0x39, 0x35);
        }

        public Rect ClampedSelection()
        {
            var v = Geom.ToRect(VirtualBounds);
            var r = Rect.Intersect(Selection, v);
            return r.IsEmpty ? Selection : r;
        }

        public void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        public void RequestCopy() { var h = CopyRequested; if (h != null) h(); }
        public void RequestSave() { var h = SaveRequested; if (h != null) h(); }
        public void RequestPin() { var h = PinRequested; if (h != null) h(); }
        public void RequestConfirm() { var h = ConfirmRequested; if (h != null) h(); }
        public void RequestCancel() { var h = CancelRequested; if (h != null) h(); }

        public void AddAnnotation(Annotation annotation)
        {
            if (annotation == null) return;
            Annotations.Add(annotation);
            _redo.Clear();
            RaiseChanged();
        }

        public bool CanUndo => Annotations.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public bool Undo()
        {
            if (Annotations.Count == 0) return false;
            var last = Annotations[Annotations.Count - 1];
            Annotations.RemoveAt(Annotations.Count - 1);
            _redo.Push(last);
            RaiseChanged();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            Annotations.Add(_redo.Pop());
            RaiseChanged();
            return true;
        }

        /// <summary>Removes the top-most annotation under the point (eraser tool).</summary>
        public bool EraseAt(Point p)
        {
            for (int i = Annotations.Count - 1; i >= 0; i--)
            {
                if (Annotations[i].HitTest(p, 6))
                {
                    _redo.Push(Annotations[i]);
                    Annotations.RemoveAt(i);
                    RaiseChanged();
                    return true;
                }
            }
            return false;
        }

        public Annotation HitTestAnnotation(Point p)
        {
            for (int i = Annotations.Count - 1; i >= 0; i--)
            {
                if (Annotations[i].HitTest(p, 6)) return Annotations[i];
            }
            return null;
        }

        /// <summary>Monitor that should host the floating toolbar.</summary>
        public MonitorInfo ToolbarMonitor()
        {
            System.Drawing.Point probe;
            if (HasSelection)
                probe = new System.Drawing.Point((int)(Selection.X + Selection.Width / 2), (int)(Selection.Y + Selection.Height / 2));
            else
                probe = new System.Drawing.Point((int)CursorGlobal.X, (int)CursorGlobal.Y);
            return MonitorInfo.FromPoint(probe) ?? MonitorInfo.Primary();
        }

        public BitmapSource ComposeResult()
        {
            return ImageComposer.Compose(Snapshot, ClampedSelection(), Annotations);
        }

        public string StatusText()
        {
            if (!HasSelection) return "拖动鼠标选择区域，或直接点击要截取的窗口";
            int w = (int)Math.Round(Selection.Width);
            int h = (int)Math.Round(Selection.Height);
            return $"{w} × {h}";
        }
    }
}