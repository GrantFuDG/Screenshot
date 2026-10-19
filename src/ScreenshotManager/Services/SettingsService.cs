using System;
using System.IO;
using System.Text.Json;
using ScreenshotManager.Models;

namespace ScreenshotManager.Services
{
    /// <summary>Loads / saves <see cref="AppSettings"/> and raises <see cref="Changed"/>.</summary>
    public sealed class SettingsService
    {
        private readonly object _gate = new object();

        public AppSettings Settings { get; private set; }

        public event EventHandler Changed;

        public SettingsService()
        {
            Settings = Load();
        }

        public static AppSettings Load()
        {
            try
            {
                AppPaths.EnsureCreated();
                if (File.Exists(AppPaths.SettingsFile))
                {
                    string json = File.ReadAllText(AppPaths.SettingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json, Json.Options);
                    if (s != null)
                    {
                        s.Normalize();
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("加载设置失败，已使用默认设置", ex);
            }
            var def = AppSettings.CreateDefault();
            def.Normalize();
            return def;
        }

        public void Save()
        {
            lock (_gate)
            {
                try
                {
                    AppPaths.EnsureCreated();
                    string json = JsonSerializer.Serialize(Settings, Json.Options);
                    string tmp = AppPaths.SettingsFile + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(AppPaths.SettingsFile)) File.Replace(tmp, AppPaths.SettingsFile, null);
                    else File.Move(tmp, AppPaths.SettingsFile);
                }
                catch (Exception ex)
                {
                    AppLog.Error("保存设置失败", ex);
                }
            }
        }

        /// <summary>Mutates settings, normalizes, persists and notifies.</summary>
        public void Update(Action<AppSettings> mutate, bool notify = true)
        {
            mutate(Settings);
            Settings.Normalize();
            Save();
            if (notify) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Replace(AppSettings fresh, bool notify = true)
        {
            fresh.Normalize();
            Settings = fresh;
            Save();
            if (notify) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void ResetToDefault()
        {
            Replace(AppSettings.CreateDefault());
        }
    }
}