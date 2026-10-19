using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotManager.Models;

namespace ScreenshotManager.Services
{
    /// <summary>Index of saved screenshots plus thumbnail generation and retention pruning.</summary>
    public sealed class HistoryService
    {
        private readonly List<ScreenshotItem> _items = new List<ScreenshotItem>();
        private readonly object _gate = new object();

        public event EventHandler Changed;

        public IReadOnlyList<ScreenshotItem> Items
        {
            get { lock (_gate) return _items.ToList(); }
        }

        public int Count { get { lock (_gate) return _items.Count; } }

        public HistoryService()
        {
            Load();
        }

        public void Load()
        {
            lock (_gate)
            {
                _items.Clear();
                try
                {
                    AppPaths.EnsureCreated();
                    if (File.Exists(AppPaths.HistoryFile))
                    {
                        string json = File.ReadAllText(AppPaths.HistoryFile);
                        var list = JsonSerializer.Deserialize<List<ScreenshotItem>>(json, Json.Options);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                if (item == null || string.IsNullOrEmpty(item.FilePath)) continue;
                                if (!File.Exists(item.FilePath)) continue;
                                _items.Add(item);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("加载历史记录失败", ex);
                }
                _items.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            }
            RaiseChanged();
        }

        public void Save()
        {
            lock (_gate)
            {
                try
                {
                    AppPaths.EnsureCreated();
                    string json = JsonSerializer.Serialize(_items, Json.Options);
                    string tmp = AppPaths.HistoryFile + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(AppPaths.HistoryFile)) File.Replace(tmp, AppPaths.HistoryFile, null);
                    else File.Move(tmp, AppPaths.HistoryFile);
                }
                catch (Exception ex)
                {
                    AppLog.Error("保存历史记录失败", ex);
                }
            }
        }

        public ScreenshotItem Add(string filePath, int width, int height, string source)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
            var item = new ScreenshotItem
            {
                FilePath = filePath,
                Width = width,
                Height = height,
                Source = source ?? "capture",
                CreatedAt = DateTime.Now
            };
            try
            {
                item.SizeBytes = new FileInfo(filePath).Length;
                item.ThumbPath = MakeThumbnail(filePath, item.Id);
            }
            catch (Exception ex)
            {
                AppLog.Warn("生成缩略图失败: " + ex.Message);
            }

            lock (_gate)
            {
                _items.Insert(0, item);
            }
            Save();
            RaiseChanged();
            return item;
        }

        public void Remove(ScreenshotItem item, bool deleteFile)
        {
            if (item == null) return;
            lock (_gate) _items.Remove(item);
            if (deleteFile)
            {
                TryDelete(item.FilePath);
                TryDelete(item.ThumbPath);
            }
            Save();
            RaiseChanged();
        }

        public void Clear(bool deleteFiles)
        {
            List<ScreenshotItem> copy;
            lock (_gate)
            {
                copy = _items.ToList();
                _items.Clear();
            }
            if (deleteFiles)
            {
                foreach (var it in copy)
                {
                    TryDelete(it.FilePath);
                    TryDelete(it.ThumbPath);
                }
            }
            Save();
            RaiseChanged();
        }

        /// <summary>
        /// Enforces the retention limits. Files are only deleted when they live inside
        /// <paramref name="ownedDirectory"/> (the configured save folder); anything saved elsewhere
        /// is dropped from the index but left on disk.
        /// </summary>
        public void Prune(int maxCount, int maxAgeDays, string ownedDirectory)
        {
            var doomed = new List<ScreenshotItem>();
            lock (_gate)
            {
                _items.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
                if (maxAgeDays > 0)
                {
                    var cutoff = DateTime.Now.AddDays(-maxAgeDays);
                    foreach (var it in _items.Where(i => i.CreatedAt < cutoff).ToList()) doomed.Add(it);
                }
                if (maxCount > 0 && _items.Count - doomed.Count > maxCount)
                {
                    int keep = maxCount;
                    foreach (var it in _items)
                    {
                        if (doomed.Contains(it)) continue;
                        if (keep-- > 0) continue;
                        doomed.Add(it);
                    }
                }
                foreach (var it in doomed) _items.Remove(it);
            }

            if (doomed.Count > 0)
            {
                string owned = null;
                try
                {
                    if (!string.IsNullOrEmpty(ownedDirectory))
                        owned = Path.GetFullPath(ownedDirectory).TrimEnd('\\') + "\\";
                }
                catch { owned = null; }

                foreach (var it in doomed)
                {
                    TryDelete(it.ThumbPath);
                    if (owned != null && it.FilePath != null)
                    {
                        try
                        {
                            string full = Path.GetFullPath(it.FilePath);
                            if (full.StartsWith(owned, StringComparison.OrdinalIgnoreCase)) TryDelete(full);
                        }
                        catch { }
                    }
                }
                Save();
                RaiseChanged();
                AppLog.Info($"历史记录清理 {doomed.Count} 条 (上限 {maxCount}, 天数 {maxAgeDays})");
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { AppLog.Warn("删除文件失败 " + path + " :: " + ex.Message); }
        }

        public static string MakeThumbnail(string imagePath, string id)
        {
            try
            {
                AppPaths.EnsureCreated();
                var src = ImageComposer.LoadFromFile(imagePath);
                const int target = 260;
                double scale = Math.Min(1.0, target / (double)Math.Max(1, src.PixelWidth));
                int w = Math.Max(1, (int)(src.PixelWidth * scale));
                int h = Math.Max(1, (int)(src.PixelHeight * scale));

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(src, new System.Windows.Rect(0, 0, w, h));
                }
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();

                string thumbPath = Path.Combine(AppPaths.ThumbsDir, id + ".png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(thumbPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(fs);
                }
                return thumbPath;
            }
            catch (Exception ex)
            {
                AppLog.Warn("缩略图生成失败: " + ex.Message);
                return null;
            }
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}