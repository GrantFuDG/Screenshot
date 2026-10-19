using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ScreenshotManager.Models;

namespace ScreenshotManager.Services
{
    /// <summary>Very small file + console logger. Console output is used by the headless self-test runs.</summary>
    public static class AppLog
    {
        private static readonly object Gate = new object();
        private static readonly List<string> Tail = new List<string>();
        public static bool ConsoleEcho { get; set; } = false;

        public static void Info(string message) => Write("INFO ", message);
        public static void Warn(string message) => Write("WARN ", message);

        public static void Warn(string message, Exception ex) => Write("WARN ", message + " :: " + ex.Message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message + " :: " + ex.GetType().Name + ": " + ex.Message);
            if (ex.StackTrace != null) Write("ERROR", ex.StackTrace);
        }

        private static void Write(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            lock (Gate)
            {
                Tail.Add(line);
                if (Tail.Count > 400) Tail.RemoveAt(0);
                if (ConsoleEcho) Console.WriteLine(line);
                try
                {
                    AppPaths.EnsureCreated();
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { /* logging must never throw */ }
            }
        }

        public static string[] Recent()
        {
            lock (Gate) return Tail.ToArray();
        }
    }
}