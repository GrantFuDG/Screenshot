using System;
using Microsoft.Win32;

namespace ScreenshotManager.Services
{
    /// <summary>Start-with-Windows via the per-user Run key (no admin rights needed).</summary>
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ScreenshotManager";

        public static string ExecutablePath
        {
            get
            {
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    var value = key?.GetValue(ValueName) as string;
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取开机启动项失败: " + ex.Message);
                return false;
            }
        }

        public static bool Set(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        error = "无法打开注册表启动项";
                        return false;
                    }
                    if (enabled)
                    {
                        string exe = ExecutablePath;
                        if (string.IsNullOrEmpty(exe))
                        {
                            error = "无法确定程序路径";
                            return false;
                        }
                        key.SetValue(ValueName, "\"" + exe + "\" --tray");
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    }
                }
                AppLog.Info("开机启动已" + (enabled ? "启用" : "禁用"));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLog.Warn("设置开机启动失败: " + ex.Message);
                return false;
            }
        }
    }
}