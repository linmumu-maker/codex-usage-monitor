using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CodexUsageMonitor
{
    internal static class StartupRegistration
    {
        internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "CodexUsageMonitor";
        internal static string Executable { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        internal static string Command(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.IndexOf('"') >= 0 || path.IndexOf('\n') >= 0 || path.IndexOf('\r') >= 0)
                throw new ArgumentException("Invalid executable path.");
            return "\"" + Path.GetFullPath(path) + "\" --background";
        }
        internal static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                    return key != null && String.Equals(key.GetValue(ValueName) as string, Command(Executable), StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath)) Write(key, Executable, enabled);
        }
        // Separate key allows tests to verify registration without touching Windows startup.
        internal static void Write(RegistryKey key, string path, bool enabled)
        {
            if (enabled) key.SetValue(ValueName, Command(path), RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
        }
    }
}
