using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor.Tests
{
    internal static class SidebarLifecycle
    {
        private static int checks, skipped;
        private static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value); }
        private static void Invoke(object value, string name, params object[] args) { value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, args); }
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        private static void Pump() { Forms.Application.DoEvents(); if (!Dispatcher.CurrentDispatcher.HasShutdownStarted) Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { })); }
        private static void Wait(Func<bool> condition, string name, int timeout = 35000)
        {
            var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < timeout) { Pump(); System.Threading.Thread.Sleep(20); }
            Check(condition(), name);
        }
        internal static int Run(bool live)
        {
            MonitorApplication app = null; bool stopped = false;
            string before = File.Exists(MonitorSettings.FilePath) ? File.ReadAllText(MonitorSettings.FilePath) : null;
            try
            {
                using (var form = new Forms.Form())
                using (var other = new Forms.Form())
                {
                    form.Text = "右侧使用量卡片生命周期测试"; form.StartPosition = Forms.FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(70, 60); form.ClientSize = new System.Drawing.Size(1500, 950);
                    var composer = new Forms.GroupBox { Text = "Fixture composer", Left = 110, Top = 740, Width = 850, Height = 120 };
                    composer.Controls.Add(new Forms.TextBox { Left = 10, Top = 24, Width = 825, Height = 25 });
                    composer.Controls.Add(new Forms.Button { Text = "Send", Left = 720, Top = 78, Width = 80, Height = 25 }); form.Controls.Add(composer);
                    var panel = new Forms.GroupBox { Text = "Fixture sidebar", Left = 1120, Top = 70, Width = 376, Height = 870 }; form.Controls.Add(panel);
                    form.Show(); Pump(); var handle = form.Handle;
                    var tracker = new WindowTracker(() => handle);
                    string path = live ? "" : Process.GetCurrentProcess().MainModule.FileName;
                    app = new MonitorApplication(false, new MonitorSettings { CodexExecutable = path, DisplayMode = "sidebarCard", Theme = "light" }, tracker);
                    var state = Field<QuotaState>(app, "state");
                    Wait(() => state.LastSuccess.HasValue || state.Error != null, "startup read completes");
                    Check(state.Error == null, "startup quota succeeds");
                    Console.WriteLine("CARD LIFECYCLE " + state.Summary(DateTimeOffset.UtcNow).Replace("\n", " | "));
                    Wait(() => tracker.Latest != null && tracker.Latest.Valid && tracker.Latest.Sidebars.Count == 1, "sidebar discovered");
                    var overlay = Field<OverlayWindow>(app, "overlay");
                    Check(overlay.Card != null && overlay.Bar == null && overlay.Owner == null, "only sidebar card is hosted without owner");
                    var service = Field<RefreshService>(app, "service"); int childId = service.Client.ProcessId;
                    Invoke(app, "HideCard"); Pump();
                    Check(Field<bool>(app, "cardHidden") && !overlay.IsVisible && Field<DispatcherTimer>(app, "follower").IsEnabled, "close card hides only display");
                    Invoke(app, "SetDisplayMode", "sidebarCard"); Pump();
                    Check(!Field<bool>(app, "cardHidden") && service.Client.ProcessId == childId, "tray restore preserves quota service");
                    overlay = Field<OverlayWindow>(app, "overlay");
                    Native.SetForegroundWindow(handle); Pump();
                    if (Native.ForegroundEligible(handle))
                    {
                        Wait(() => overlay.IsVisible, "sidebar shown in safe region");
                        Check(Native.GetForegroundWindow() == handle, "card placement does not steal foreground");
                        Native.RECT bounds; Native.GetWindowRect(overlay.Handle, out bounds);
                        Check(bounds.Right - bounds.Left >= 300 && bounds.Right - bounds.Left <= 480, "card actual width follows sidebar");
                        var child = service.Client.ProcessId; var previous = state.LastSuccess;
                        Invoke(app, "Refresh"); Wait(() => state.LastSuccess != previous && !state.Refreshing, "card manual refresh completes");
                        Check(service.Client.ProcessId == child && Native.GetForegroundWindow() == handle, "manual refresh preserves child and focus");
                        var blocked = new Forms.Label { Text = "Fixture occupied content", Left = 5, Top = 25, Width = 365, Height = 835 }; panel.Controls.Add(blocked); tracker.RequestScan();
                        Wait(() => !overlay.IsVisible && Field<string>(app, "positionReason").Contains("原生内容"), "native panel content hides card");
                        blocked.Dispose(); tracker.RequestScan(); Wait(() => overlay.IsVisible, "removed content restores card");
                        panel.Visible = false; tracker.RequestScan(); Wait(() => !overlay.IsVisible, "collapsed sidebar hides card");
                        panel.Visible = true; tracker.RequestScan(); Wait(() => overlay.IsVisible, "opened sidebar restores card");
                        form.WindowState = Forms.FormWindowState.Minimized; tracker.RequestScan(); Wait(() => !overlay.IsVisible, "minimized target hides card");
                        form.WindowState = Forms.FormWindowState.Normal; Native.SetForegroundWindow(handle); tracker.RequestScan(); Pump();
                        other.Text = "Other application fixture"; other.Show(); Native.SetForegroundWindow(other.Handle); Pump();
                        if (Native.GetForegroundWindow() == other.Handle) Wait(() => !overlay.IsVisible, "other foreground hides card");
                        else { skipped++; Console.WriteLine("SKIP other foreground activation denied"); }
                        other.Hide();
                    }
                    else { skipped++; Console.WriteLine("SKIP card foreground paths: activation denied"); }
                    var selector = new SidebarCalibration(handle, region => { throw new Exception("Escape must not save"); });
                    selector.Show(); Pump();
                    selector.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(selector), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump();
                    Check(!selector.IsVisible, "calibration Escape cancels without saving");
                    Invoke(app, "ShowDetails"); Pump(); var details = Field<StatusWindow>(app, "details"); details.WindowState = WindowState.Minimized;
                    Check(Field<DispatcherTimer>(app, "follower").IsEnabled, "minimized status keeps card follower running");
                    if (live)
                    {
                        int automatic = 0; Action<QuotaSnapshot, string, bool> completed = (snapshot, error, isAuto) => { if (isAuto) System.Threading.Interlocked.Exchange(ref automatic, error == null ? 1 : -1); };
                        service.Completed += completed; Console.WriteLine("CARD waiting for 60-second automatic refresh");
                        Wait(() => System.Threading.Volatile.Read(ref automatic) != 0, "automatic read completes with minimized status", 75000);
                        service.Completed -= completed; Check(automatic == 1, "real automatic refresh succeeds");
                    }
                    details.Close(); Invoke(app, "SetDisplayMode", "compactBar"); Pump();
                    Check(Field<OverlayWindow>(app, "overlay").Bar != null && service.Client.ProcessId == childId, "compact fallback reuses quota child");
                    app.Shutdown(); stopped = true; Pump();
                    try { var child = Process.GetProcessById(childId); Wait(() => child.HasExited, "exit releases quota child", 5000); child.Dispose(); }
                    catch (ArgumentException) { Check(true, "quota child already exited"); }
                }
                string report = "PASS card lifecycle checks=" + checks + "\nForeground paths skipped=" + skipped + "\nLive auto refresh=" + live + "\nCheckedLocal=" + DateTimeOffset.Now.ToString("o");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, live ? "sidebar-lifecycle-live.txt" : "sidebar-lifecycle.txt"), report);
                Console.WriteLine(report); return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL card lifecycle " + error.Message + "\n" + error.StackTrace); return 1; }
            finally
            {
                if (app != null && !stopped) { app.Shutdown(); Pump(); }
                if (before == null) { if (File.Exists(MonitorSettings.FilePath)) File.Delete(MonitorSettings.FilePath); }
                else File.WriteAllText(MonitorSettings.FilePath, before);
            }
        }
    }
}
