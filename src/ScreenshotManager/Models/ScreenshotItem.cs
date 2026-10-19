using System;

namespace ScreenshotManager.Models
{
    /// <summary>One entry in the screenshot history.</summary>
    public sealed class ScreenshotItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FilePath { get; set; }
        public string ThumbPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        /// <summary>capture | import</summary>
        public string Source { get; set; } = "capture";
        public bool Pinned { get; set; }

        public string FileName => string.IsNullOrEmpty(FilePath) ? "" : System.IO.Path.GetFileName(FilePath);

        public string DisplaySize
        {
            get
            {
                double kb = SizeBytes / 1024.0;
                if (kb < 1024) return kb.ToString("0.#") + " KB";
                return (kb / 1024.0).ToString("0.##") + " MB";
            }
        }

        public string DisplayDate => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    }
}