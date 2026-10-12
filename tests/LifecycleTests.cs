using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor.Tests
{
    internal static class LifecycleTests
    {
        private static int checks, skipped;
        internal static void PolicyTests(Action<bool, string> assert)
        {
            assert(OverlayVisibility.HiddenReason(false, false, false, false, false).Contains("等待"), "missing target reason");
            assert(OverlayVisibility.HiddenReason(true, true, true, false, true).Contains("已最小化"), "Codex minimized takes priority");
            assert(OverlayVisibility.HiddenReason(true, false, false, false, false).Contains("不可见"), "invisible target reason");
            assert(OverlayVisibility.HiddenReason(true, false, true, false, true).Contains("监控状态页"), "monitor foreground has specific reason");
            assert(OverlayVisibility.HiddenReason(true, false, true, false, false).Contains("不在前台"), "other foreground has specific reason");
            assert(OverlayVisibility.HiddenReason(true, false, true, true, false) == null, "Codex foreground eligible independently of status page");
            var history = new PlacementHistory();
            var now = DateTimeOffset.UtcNow;
            assert(history.Description == null, "no invented history");
            history.Observe(new IntPtr(1), true, "自动定位：输入区上方", now);
            string success = history.Description;
            assert(success.Contains("自动定位") && success.Contains(now.ToLocalTime().ToString("MM-dd HH:mm:ss")), "history records result and local time");
            history.Observe(new IntPtr(1), false, "监控状态页在前台", now.AddSeconds(5));
            assert(history.Description == success, "monitor foreground does not overwrite history");
            history.Observe(new IntPtr(1), true, "上方位置与聊天内容重叠", now.AddSeconds(6));
            assert(history.Description.Contains("重叠"), "foreground failure remains diagnosable");
            history.Observe(new IntPtr(2), false, "Codex 不在前台", now.AddSeconds(7));
            assert(history.Description == null, "different target clears unrelated history");
            history.Observe(IntPtr.Zero, true, "等待 Codex 窗口", now);
            assert(history.Description == null, "missing target cannot create history");
            history.Observe(new IntPtr(2), true, "自动定位", now); history.Clear();
            assert(history.Description == null, "settings change clears obsolete history");
        }
        internal static int Run(bool live, bool background = false)
        {
            try
            {
                PolicyTests(Check);
                Fixture(live, background);
                string report = "PASS lifecycle checks=" + checks + "\nForeground paths skipped=" + skipped +
                    "\nLive 60-second minimized refresh=" + live + "\nCheckedLocal=" + DateTimeOffset.Now.ToString("o");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, background ? "startup-lifecycle-results.txt" : (live ? "lifecycle-live-results.txt" : "lifecycle-results.txt")), report);
                Console.WriteLine(report);
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL lifecycle " + error.GetType().Name + ": " + error.Message + "\n" + error.StackTrace);
                return 1;
            }
        }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            checks++;
        }
        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
        }
        private static void Call(object instance, string name)
        {
            instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, null);
        }
        private static void Pump()
        {
            Forms.Application.DoEvents();
            if (!Dispatcher.CurrentDispatcher.HasShutdownStarted)
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
        }
        private static void Wait(Func<bool> ready, int timeout, string name)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.ElapsedMilliseconds < timeout)
            {
                Pump(); System.Threading.Thread.Sleep(20);
            }
            Check(ready(), name);
        }
        private static void RefreshAndWait(MonitorApplication app, QuotaState state, string name)
        {
            var previous = state.LastSuccess;
            Call(app, "Refresh");
            Wait(() => state.LastSuccess.HasValue && state.LastSuccess != previous && !state.Refreshing, 35000, name);
            Check(state.Error == null, name + " succeeds");
        }
        private static void Fixture(bool live, bool background)
        {
            string configBefore = File.Exists(MonitorSettings.FilePath) ? File.ReadAllText(MonitorSettings.FilePath) : null;
            MonitorApplication app = null;
            bool shutdown = false;
            Process child = null;
            using (var form = new Forms.Form())
            using (var other = new Forms.Form())
            {
                try
                {
                    form.Text = "额度监控窗口生命周期测试";
                    form.StartPosition = Forms.FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(100, 80);
                    form.ClientSize = new System.Drawing.Size(1100, 850);
                    var composer = new Forms.GroupBox { Text = "Fixture input area", Left = 180, Top = 680, Width = 880, Height = 120 };
                    var input = new Forms.TextBox { Left = 16, Top = 22, Width = 846, Height = 24 };
                    var send = new Forms.Button { Text = "Send", Left = 740, Top = 76, Width = 90, Height = 28 };
                    composer.Controls.Add(input); composer.Controls.Add(send); form.Controls.Add(composer);
                    form.Show(); Pump();
                    IntPtr handle = form.Handle;
                    string executable = Process.GetCurrentProcess().MainModule.FileName;
                    if (live)
                    {
                        string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "CodexUsageMonitor", "settings.json"));
                        executable = File.Exists(path) ? Json.Get(Json.Parse(File.ReadAllText(path)), "CodexExecutable") as string ?? "" : "";
                    }
                    bool detected = !background;
                    int scans = 0;
                    var tracker = new WindowTracker(() => { System.Threading.Interlocked.Increment(ref scans); return detected ? handle : IntPtr.Zero; });
                    app = new MonitorApplication(false, new MonitorSettings { CodexExecutable = executable, Theme = "light", DisplayMode = "compactBar" }, tracker, background);
                    if (background)
                    {
                        Wait(() => System.Threading.Volatile.Read(ref scans) > 0, 5000, "idle scanner completes without Codex");
                        Call(app, "EvaluateOverlay");
                        Check(Field<RefreshService>(app, "service") == null, "background waits without launching App Server");
                        Check(Field<StatusWindow>(app, "details") == null, "background has no status popup");
                        detected = true; tracker.RequestScan();
                    }
                    var state = Field<QuotaState>(app, "state");
                    Wait(() => state.LastSuccess.HasValue || state.Error != null, 40000, "startup read completes");
                    Check(state.Error == null && state.Snapshot != null, "startup quota succeeds");
                    if (live) Check(state.Snapshot.FiveHour != null && state.Snapshot.Weekly != null, "real account windows returned");
                    Console.WriteLine("LIFECYCLE startup " + state.Summary(DateTimeOffset.UtcNow).Replace("\n", " | "));
                    var service = Field<RefreshService>(app, "service");
                    int childId = service.Client.ProcessId;
                    child = Process.GetProcessById(childId);
                    Wait(() => tracker.Latest != null && tracker.Latest.Valid, 35000, "background fixture scan completes");
                    Call(app, "ShowDetails"); Pump();
                    var details = Field<StatusWindow>(app, "details");
                    var overlay = Field<OverlayWindow>(app, "overlay");
                    Check(details.Owner == null && overlay.Owner == null, "status page and overlay have independent ownership");
                    Check(app.MainWindow == overlay && app.ShutdownMode == ShutdownMode.OnExplicitShutdown, "only explicit exit shuts down monitor");
                    details.WindowState = WindowState.Minimized; Pump();
                    Check(details.WindowState == WindowState.Minimized, "status page minimized");
                    Check(overlay.WindowState == WindowState.Normal, "status minimize does not minimize overlay");
                    Check(Field<DispatcherTimer>(app, "follower").IsEnabled && Field<Forms.NotifyIcon>(app, "tray").Visible, "minimize keeps tracking and tray alive");
                    RefreshAndWait(app, state, "manual refresh while status minimized");
                    Check(details.WindowState == WindowState.Minimized && service.Client.ProcessId == childId, "refresh preserves minimized status and child process");

                    Native.SetForegroundWindow(handle); Pump();
                    if (Native.ForegroundEligible(handle))
                    {
                        Wait(() => overlay.IsVisible, 35000, "overlay recovers with minimized status and eligible target");
                        Check(Native.GetForegroundWindow() == handle, "recovery does not steal focus");
                        Native.RECT bounds; Native.GetWindowRect(overlay.Handle, out bounds);
                        Check(bounds.Bottom <= tracker.Latest.Composer.Top, "recovered overlay remains above composer");
                        Check(Field<PlacementHistory>(app, "placementHistory").Description != null, "actual eligible evaluation records history");
                        var collision = new Forms.Label { Text = "Occupied fixture content", Left = 210, Top = 638, Width = 250, Height = 30 };
                        form.Controls.Add(collision); tracker.RequestScan();
                        Wait(() => overlay.IsVisible && Field<string>(app, "positionReason").Contains("避让"), 35000, "nearby controls avoided while monitor minimized");
                        Native.GetWindowRect(overlay.Handle, out bounds);
                        Check(bounds.Bottom < form.PointToScreen(collision.Location).Y, "avoided bar remains above visible content");
                        collision.Top = 400; collision.Height = 280; tracker.RequestScan();
                        Wait(() => !overlay.IsVisible && Field<string>(app, "positionReason").Contains("安全空白"), 35000, "fully occupied band still hides while minimized");
                        collision.Dispose(); tracker.RequestScan();
                        Wait(() => overlay.IsVisible, 35000, "safe layout recovers after collision removed");
                        other.Text = "Other application fixture"; other.Show(); Native.SetForegroundWindow(other.Handle); Pump();
                        if (Native.GetForegroundWindow() == other.Handle)
                        {
                            Wait(() => !overlay.IsVisible, 3000, "foreground event hides overlay");
                            Native.SetForegroundWindow(handle); Pump();
                            if (Native.ForegroundEligible(handle))
                                Wait(() => overlay.IsVisible, 35000, "foreground event restores overlay");
                            else { skipped++; Console.WriteLine("SKIP target foreground restore refused"); }
                        }
                        else { skipped++; Console.WriteLine("SKIP other foreground activation refused"); }
                    }
                    else { skipped++; Console.WriteLine("SKIP overlay foreground scenarios: Windows activation refused"); }

                    form.WindowState = Forms.FormWindowState.Minimized; Pump();
                    Wait(() => !overlay.IsVisible && Field<string>(app, "positionReason").Contains("Codex 已最小化"), 35000, "target minimize has distinct reason");
                    form.WindowState = Forms.FormWindowState.Normal; tracker.RequestScan(); Pump();
                    Call(app, "ShowDetails"); Pump();
                    Check(Field<StatusWindow>(app, "details") == details && details.WindowState == WindowState.Normal, "tray reopening restores the same minimized status page");
                    var statusHandle = new WindowInteropHelper(details).Handle;
                    if (Native.GetForegroundWindow() == statusHandle)
                        Wait(() => Field<string>(app, "positionReason").Contains("监控状态页"), 35000, "status foreground has distinct reason");
                    else { skipped++; Console.WriteLine("SKIP status foreground activation refused"); }

                    details.WindowState = WindowState.Minimized; Pump();
                    if (live)
                    {
                        int automaticResult = 0;
                        Action<QuotaSnapshot, string, bool> automaticRead = (snapshot, error, automatic) =>
                        {
                            if (automatic) System.Threading.Interlocked.Exchange(ref automaticResult, error == null ? 1 : -1);
                        };
                        service.Completed += automaticRead;
                        var beforeAutomatic = state.LastSuccess;
                        Console.WriteLine("LIFECYCLE waiting for real 60-second automatic refresh with status minimized");
                        Wait(() => System.Threading.Volatile.Read(ref automaticResult) != 0 && state.LastSuccess.HasValue &&
                            state.LastSuccess != beforeAutomatic && !state.Refreshing, 75000, "real automatic refresh while status minimized");
                        service.Completed -= automaticRead;
                        Check(automaticResult == 1 && state.Error == null && details.WindowState == WindowState.Minimized,
                            "real automatic read succeeds without restoring status page");
                        Console.WriteLine("LIFECYCLE automatic " + state.Summary(DateTimeOffset.UtcNow).Replace("\n", " | "));
                    }
                    details.Close(); Pump();
                    Check(Field<StatusWindow>(app, "details") == null, "closing status only removes status page");
                    Check(Field<DispatcherTimer>(app, "follower").IsEnabled && Field<Forms.NotifyIcon>(app, "tray").Visible, "closing status keeps monitoring alive");
                    RefreshAndWait(app, state, "manual refresh after status closes");
                    Check(service.Client.ProcessId == childId, "closing status does not restart quota child");
                    Call(app, "ShowDetails"); Pump();
                    var reopened = Field<StatusWindow>(app, "details");
                    Check(reopened != details && reopened.WindowState == WindowState.Normal, "closed status can be opened again");
                    reopened.Close(); Pump();

                    app.Shutdown(); shutdown = true; Pump();
                    Wait(() => child.HasExited, 5000, "explicit exit releases its App Server child");
                    Check(!Field<DispatcherTimer>(app, "follower").IsEnabled, "explicit exit stops tracking timer");
                    Check((File.Exists(MonitorSettings.FilePath) ? File.ReadAllText(MonitorSettings.FilePath) : null) == configBefore, "lifecycle fixture preserves configuration");
                }
                finally
                {
                    if (app != null && !shutdown) { app.Shutdown(); Pump(); }
                    if (child != null) child.Dispose();
                }
            }
        }
    }
}
