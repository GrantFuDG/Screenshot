using System;
using System.IO;
using System.Text;
using System.Windows;
using ScreenshotManager.Models;
using ScreenshotManager.Services;

namespace ScreenshotManager
{
    /// <summary>进程入口：装好 WPF 消息泵后交给 <see cref="AppHost"/> 接线。</summary>
    internal static class Program
    {
        /// <summary>兜底启动日志：AppLog 就绪之前的任何异常都写这里（%TEMP%\\screenshotmanager-startup.log）。</summary>
        internal static readonly string StartupTracePath =
            Path.Combine(Path.GetTempPath(), "screenshotmanager-startup.log");

        /// <summary>用户配置目录不可写时的兜底位置：程序自身目录（便于排查绿色版/受限环境）。</summary>
        internal static readonly string FallbackTracePath =
            Path.Combine(AppContext.BaseDirectory, "startup-trace.txt");

        [STAThread]
        private static int Main(string[] args)
        {
            Trace("Main 进入 args=" + (args == null ? 0 : args.Length)
                + " 用户=" + Environment.UserName
                + " cwd=" + Environment.CurrentDirectory
                + " TEMP=" + Path.GetTempPath()
                + " APPDATA=" + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                + " base=" + AppContext.BaseDirectory);

            try
            {
                AppDomain.CurrentDomain.UnhandledException +=
                    (s, e) => Report("非 UI 线程未处理异常", e.ExceptionObject as Exception);
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException +=
                    (s, e) => Report("未观察的任务异常", e.Exception);

                AppPaths.EnsureCreated();
                Trace("配置目录就绪: " + AppPaths.RootDir);
                AppLog.Info("截图管家启动，配置目录 " + AppPaths.RootDir
                    + (AppPaths.IsPortable ? "（绿色模式：%APPDATA% 不可写，已回退到程序目录）" : ""));

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    // 托盘程序不该因为一个 UI 异常就整体崩掉；记日志并继续运行。
                    Report("UI 线程未处理异常", e.Exception);
                    e.Handled = true;
                };

                AppHost host = null;
                try
                {
                    host = new AppHost(app);
                }
                catch (Exception ex)
                {
                    Report("初始化失败（AppHost 构造）", ex);
                    return 1;
                }

                try
                {
                    if (Environment.GetEnvironmentVariable("SM_SELFTEST") == "1")
                    {
                        // 诊断用：无人值守地把各窗口打开一遍，验证 UI 代码路径后立即退出。
                        app.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                host.RunSelfTest();
                            }
                            finally
                            {
                                app.Shutdown();
                            }
                        }));
                    }

                    app.Run();
                }
                finally
                {
                    host.Dispose();
                    AppLog.Info("截图管家退出");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Report("启动失败", ex);
                return 1;
            }
        }

        private static void Trace(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [TRACE] " + message + Environment.NewLine;
            foreach (string target in new[] { StartupTracePath, FallbackTracePath })
            {
                try
                {
                    File.AppendAllText(target, line, Encoding.UTF8);
                }
                catch
                {
                    // 兜底日志本身绝不能再抛异常。
                }
            }
        }

        private static void Report(string title, Exception ex)
        {
            string text = ex == null ? "(无异常对象)" : ex.ToString();
            try
            {
                AppLog.Error(title + " :: " + text);
            }
            catch
            {
            }
            Trace(title + " :: " + text);
            try
            {
                MessageBox.Show(
                    title + "：" + Environment.NewLine + text,
                    "截图管家", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
            }
        }
    }
}