using System;
using Microsoft.Win32;

namespace CodexUsageMonitor.Tests
{
    internal static class StartupTests
    {
        internal static int Idle()
        {
            try
            {
                var app = new MonitorApplication(false, new MonitorSettings { Theme = "light" }, new WindowTracker(() => IntPtr.Zero), true);
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                timer.Tick += delegate
                {
                    timer.Stop();
                    var field = typeof(MonitorApplication).GetField("service", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field.GetValue(app) != null) throw new Exception("idle launched quota service");
                    app.Shutdown();
                };
                timer.Start(); app.Run();
                Console.WriteLine("PASS background idle exits normally without App Server");
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL idle " + error.GetType().Name); return 1; }
        }
        internal static void Run(Action<bool, string> assert)
        {
            string path = @"C:\Users\Test User\额度监控\CodexUsageMonitor.exe";
            string command = StartupRegistration.Command(path);
            assert(command == "\"" + path + "\" --background", "startup quotes spaced and Chinese path");
            bool rejected = false;
            try { StartupRegistration.Command("broken\"path.exe"); } catch (ArgumentException) { rejected = true; }
            assert(rejected, "startup rejects embedded quote");
            string branch = @"Software\CodexUsageMonitorTests\" + Guid.NewGuid().ToString("N");
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(branch))
                {
                    key.SetValue("Unrelated", "preserved");
                    StartupRegistration.Write(key, path, true);
                    assert((string)key.GetValue(StartupRegistration.ValueName) == command, "startup registration stores background command");
                    StartupRegistration.Write(key, path, false);
                    assert(key.GetValue(StartupRegistration.ValueName) == null, "startup removal deletes own entry");
                    assert((string)key.GetValue("Unrelated") == "preserved", "startup preserves other entries");
                    StartupRegistration.Write(key, path, false);
                    assert(key.GetValue(StartupRegistration.ValueName) == null, "startup removal is idempotent");
                }
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(branch, false); }
        }
    }
}
