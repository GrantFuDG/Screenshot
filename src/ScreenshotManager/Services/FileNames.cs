using System;
using System.Globalization;
using System.Text;
using ScreenshotManager.Models;

namespace ScreenshotManager.Services
{
    /// <summary>Expands the configured file-name template, e.g. "Shot_{yyyyMMdd_HHmmss}".</summary>
    public static class FileNames
    {
        public static string BuildName(AppSettings settings, string fallbackPrefix)
        {
            string template = settings?.FileNameTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                template = (string.IsNullOrWhiteSpace(fallbackPrefix) ? "Shot" : fallbackPrefix) + "_{yyyyMMdd_HHmmss}";
            }

            var now = DateTime.Now;
            var sb = new StringBuilder(template);
            sb.Replace("{yyyy}", now.ToString("yyyy", CultureInfo.InvariantCulture));
            sb.Replace("{yy}", now.ToString("yy", CultureInfo.InvariantCulture));
            sb.Replace("{MM}", now.ToString("MM", CultureInfo.InvariantCulture));
            sb.Replace("{dd}", now.ToString("dd", CultureInfo.InvariantCulture));
            sb.Replace("{HH}", now.ToString("HH", CultureInfo.InvariantCulture));
            sb.Replace("{mm}", now.ToString("mm", CultureInfo.InvariantCulture));
            sb.Replace("{ss}", now.ToString("ss", CultureInfo.InvariantCulture));
            sb.Replace("{fff}", now.ToString("fff", CultureInfo.InvariantCulture));
            sb.Replace("{yyyyMMdd_HHmmss}", now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            sb.Replace("{yyyy-MM-dd_HH-mm-ss}", now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            sb.Replace("{GUID}", Guid.NewGuid().ToString("N").Substring(0, 8));
            sb.Replace("{prefix}", string.IsNullOrWhiteSpace(fallbackPrefix) ? "Shot" : fallbackPrefix);

            string name = Sanitize(sb.ToString());
            if (string.IsNullOrWhiteSpace(name)) name = "Shot_" + now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            return name;
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool bad = false;
                foreach (char iv in invalid)
                {
                    if (c == iv)
                    {
                        bad = true;
                        break;
                    }
                }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString().Trim().TrimEnd('.');
        }
    }
}