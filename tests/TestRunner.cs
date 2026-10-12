using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor.Tests
{
    internal static class TestRunner
    {
        private static int assertions;
        private static readonly List<string> results = new List<string>();
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "app-server") { FakeServer(); return 0; }
            if (args.Length > 0 && args[0] == "--probe")
            {
                using (var client = new AppServerClient(args.Length > 1 ? args[1] : ""))
                {
                    try { var snapshot = client.Read().GetAwaiter().GetResult(); Console.WriteLine("PROBE remaining=" + QuotaState.Percent(snapshot.FiveHour) + " weekly=" + QuotaState.Percent(snapshot.Weekly) + " resetCards=" + (snapshot.ResetCards.HasValue ? snapshot.ResetCards.Value.ToString() : "missing")); return 0; }
                    catch (Exception failure) { Console.WriteLine("PROBE type=" + failure.GetType().Name + " startup=" + client.LastStartupDiagnostic + " executable=" + client.ResolvedExecutable + " starts=" + client.StartCount); return 1; }
                }
            }
            if (Array.IndexOf(args, "--sidebar-lifecycle") >= 0) return SidebarLifecycle.Run(Array.IndexOf(args, "--sidebar-live") >= 0);
            if (Array.IndexOf(args, "--sidebar-ui") >= 0) return SidebarTests.Render(Array.IndexOf(args, "--sidebar-live") >= 0);
            if (Array.IndexOf(args, "--startup-idle") >= 0) return StartupTests.Idle();
            if (Array.IndexOf(args, "--startup") >= 0) return LifecycleTests.Run(false, true);
            if (Array.IndexOf(args, "--lifecycle") >= 0 || Array.IndexOf(args, "--lifecycle-live") >= 0)
                return LifecycleTests.Run(Array.IndexOf(args, "--lifecycle-live") >= 0);
            if (Array.IndexOf(args, "--ui") >= 0) return UIReview.Run(Array.IndexOf(args, "--ui-live") >= 0);
            try
            {
                Console.WriteLine("TEST unit"); UnitTests(); LifecycleTests.PolicyTests(Assert); StartupTests.Run(Assert); PlacementSearchTests.Run(Assert); SidebarTests.Unit(Assert);
                Console.WriteLine("TEST protocol"); ProtocolTests();
                Console.WriteLine("TEST native fixture"); FixtureTests();
                if (Array.IndexOf(args, "--live") >= 0) LiveTests();
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results.txt");
                File.WriteAllLines(report, results.Concat(new[] { "PASS assertions=" + assertions, "CompletedLocal=" + DateTimeOffset.Now.ToString("o") }).ToArray());
                Console.WriteLine("PASS assertions=" + assertions);
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL type=" + error.GetType().Name + " test=" + error.Message); Console.WriteLine(error.StackTrace);
                return 1;
            }
        }
        private static void Assert(bool value, string name)
        {
            if (!value) throw new Exception(name);
            assertions++;
        }
        private static QuotaSnapshot Parse(string json) { return QuotaSnapshot.Read(Json.Parse(json)); }
        private static string Payload(double used, bool weekly)
        {
            return Json.Serialize(new { rateLimitsByLimitId = new { codex = new {
                primary = new { usedPercent = used, windowDurationMins = 300, resetsAt = 1893456000L },
                secondary = weekly ? new { usedPercent = 82, windowDurationMins = 10080, resetsAt = 1894060800L } : null
            } } });
        }
        private static void UnitTests()
        {
            Assert(Native.IsDesktopExecutable(@"C:\Program Files\WindowsApps\OpenAI.Codex_1_x64\app\ChatGPT.exe"), "codex package desktop alias");
            Assert(Native.IsDesktopExecutable(@"C:\Apps\Codex.exe"), "codex desktop filename");
            Assert(!Native.IsDesktopExecutable(@"C:\Apps\ChatGPT.exe"), "ordinary ChatGPT not Codex");
            var good = Parse(Payload(28, true));
            Assert(good.FiveHour.Remaining == 72, "used to remaining");
            Assert(good.Weekly.Remaining == 18, "weekly remaining");
            Assert(Parse(Payload(120, false)).FiveHour.Remaining == 0, "clamp negative remaining");
            Assert(Parse(Payload(-20, false)).FiveHour.Remaining == 100, "clamp >100 remaining");
            Assert(Parse("{}").FiveHour == null, "empty response no data");
            Assert(Parse("{\"rateLimits\":null}").Weekly == null, "null bucket no data");
            Assert(Parse("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300}}}").FiveHour.Remaining == null, "missing used not zero");
            Assert(Parse("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":15,\"usedPercent\":25}}}").FiveHour == null, "unknown duration not five hour");
            Assert(Parse("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":10080,\"usedPercent\":10},\"secondary\":{\"windowDurationMins\":300,\"usedPercent\":11}}}").FiveHour.Remaining == 89, "duration beats primary secondary order");
            Assert(Parse("{\"rateLimitsByLimitId\":{\"codex_other\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":1}}}}").FiveHour == null, "other model bucket ignored");
            Assert(Parse("{\"rateLimitsByLimitId\":{\"codex\":null},\"rateLimits\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":1}}}").FiveHour == null, "explicit missing codex not stale legacy");
            Assert(Parse("{\"rateLimits\":{\"limitId\":\"other\",\"primary\":{\"windowDurationMins\":300,\"usedPercent\":1}}}").FiveHour == null, "legacy different bucket ignored");
            Assert(Parse("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":\"0\",\"resetsAt\":-1}}}").FiveHour.Remaining == null, "invalid numeric type no data");
            Assert(Parse("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":0}}}").FiveHour.ResetText == "暂无数据", "missing reset");
            Assert(Time.LocalReset(null) == null && Time.LocalReset(long.MaxValue) == null, "timestamp range");
            var local = Time.LocalReset(1893456000L).Value;
            Assert(local.Offset == TimeZoneInfo.Local.GetUtcOffset(local.UtcDateTime), "windows timezone offset");
            Assert(local.UtcDateTime == new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), "unix seconds conversion");
            var state = new QuotaState(); var now = DateTimeOffset.UtcNow;
            state.Success(good, now); Assert(!state.IsStale(now), "success fresh");
            state.Failure("query failed"); Assert(state.IsStale(now) && state.Snapshot == good, "failure preserves last data stale");
            state.Success(Parse("{}"), now); Assert(state.Snapshot.FiveHour == null && !state.IsStale(now), "successful missing replaces old");
            state.Success(good, now); Assert(state.IsStale(now.AddSeconds(91)), "age expiry");
            state.Clear(); Assert(state.Snapshot == null && state.LastSuccess == null, "account change clears old account");
            var layout = new LayoutSnapshot { Window = new IntPtr(1), Client = new Rect(-1800, 20, 1100, 850), Composer = new Rect(-1620, 700, 880, 120), Captured = now, Scale = 1, Valid = true };
            string reason;
            Rect rect = Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason);
            Assert(!rect.IsEmpty && rect.Bottom == layout.Composer.Top - 4, "above entire composer");
            Rect moved = Placement.Calculate(layout, new Rect(-1700, 40, 1100, 850), new MonitorSettings(), out reason);
            Assert(moved.X == rect.X + 100 && moved.Y == rect.Y + 20, "negative coordinates and translated move");
            Assert(Placement.Calculate(layout, new Rect(0, 0, 700, 850), new MonitorSettings(), out reason).IsEmpty, "resize invalidates");
            layout.Occupied.Add(new Rect(rect.X + 1, rect.Y + 1, 20, 20));
            var avoided = Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason);
            Assert(!avoided.IsEmpty && avoided.Bottom < layout.Occupied[0].Top && !avoided.IntersectsWith(layout.Occupied[0]), "nearby text avoided upward");
            layout.Occupied.Add(new Rect(rect.X, layout.Client.Top, rect.Width, layout.Composer.Top - layout.Client.Top));
            Assert(Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason).IsEmpty, "fully occupied band still hidden");
            layout.Occupied.Clear();
            layout.Scale = 1.5; layout.Client = new Rect(0, 0, 1700, 1400); layout.Composer = new Rect(250, 1100, 1400, 200);
            rect = Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason);
            Assert(rect.Width == 1260 && rect.Height == 63 && rect.Bottom == 1094, "150 percent DPI physical geometry");
            layout.Client = new Rect(0, 0, 1200, 1400);
            Assert(Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason).IsEmpty, "DPI narrow window hides");
            var settings = new MonitorSettings { PositionMode = "relative", RelativeCalibrated = true, RelativeBottom = 0 };
            layout.Scale = 1; layout.Client = new Rect(0, 0, 1100, 850); layout.Composer = new Rect(180, 680, 880, 120);
            Assert(Placement.Calculate(layout, layout.Client, settings, out reason).IsEmpty, "relative calibration cannot invade composer");
            results.Add("PASS unit: quota, nulls, duration/bucket mapping, local time, stale/recovery, placement/DPI/collision");
        }
        private static void PumpUntil(Func<bool> ready, int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.ElapsedMilliseconds < milliseconds) { Forms.Application.DoEvents(); Thread.Sleep(20); }
            Assert(ready(), "wait condition timeout");
        }
        private static void ProtocolTests()
        {
            string own = Process.GetCurrentProcess().MainModule.FileName;
            Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_CASE", "normal");
            using (var client = new AppServerClient(own))
            {
                var read = client.Read(); PumpUntil(() => read.IsCompleted, 20000);
                Assert(read.Result.FiveHour.Remaining == 72, "fake server handshake parse");
                var second = client.Read(); PumpUntil(() => second.IsCompleted, 20000);
                Assert(second.Result.Weekly.Remaining == 18 && client.StartCount == 1, "persistent child no restart");
                client.KillForTest(); Thread.Sleep(100);
                var next = client.Read(); PumpUntil(() => next.IsCompleted, 20000);
                Assert(next.Result.FiveHour.Remaining == 72 && client.StartCount == 2, "child recovery");
            }
            Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_CASE", "error");
            using (var client = new AppServerClient(own))
            {
                var read = client.Read(); PumpUntil(() => read.IsCompleted, 20000);
                Assert(read.IsFaulted && read.Exception.InnerException is RpcFailure, "RPC error safe classification");
            }
            Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_CASE", null);
            results.Add("PASS protocol: initialize/initialized, correlated read, persistent child, process recovery, RPC error");
        }
        private static void FakeServer()
        {
            var reader = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false), false);
            var writer = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == (char)0xFEFF) continue;
                var request = Json.Parse(line); string method = Json.Get(request, "method") as string;
                object id = Json.Get(request, "id");
                if (method == "initialize") writer.WriteLine(Json.Serialize(new { id = id, result = new { userAgent = "fixture" } }));
                if (method == "account/rateLimits/read")
                {
                    if (Environment.GetEnvironmentVariable("CODEX_MONITOR_TEST_CASE") == "error")
                        writer.WriteLine(Json.Serialize(new { id = id, error = new { code = -32000, message = "fixture error" } }));
                    else
                    {
                        writer.WriteLine(Json.Serialize(new { id = 999999, result = new { } })); // Unrelated response ignored.
                        writer.WriteLine(Json.Serialize(new { id = id, result = Json.Parse(Payload(28, true)) }));
                    }
                }
            }
        }
        private static LayoutSnapshot ScanWithPump(IntPtr window)
        {
            var task = Task.Run(() => LayoutDetector.Scan(window));
            PumpUntil(() => task.IsCompleted, 30000);
            return task.Result;
        }
        private static void FixtureTests()
        {
            IntPtr previous = Native.GetForegroundWindow();
            using (var form = new Forms.Form())
            using (var other = new Forms.Form())
            {
                form.Text = "Codex 额度监控 · 安全定位测试窗口";
                form.StartPosition = Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(100, 80);
                form.ClientSize = new System.Drawing.Size(1100, 850);
                var composer = new Forms.GroupBox { Text = "Test input area", Left = 180, Top = 680, Width = 880, Height = 120 };
                var input = new Forms.TextBox { Multiline = false, Left = 16, Top = 22, Width = 846, Height = 24 };
                var send = new Forms.Button { Text = "Send", Left = 740, Top = 76, Width = 90, Height = 28 };
                composer.Controls.Add(input); composer.Controls.Add(send); form.Controls.Add(composer);
                form.Show(); Native.SetForegroundWindow(form.Handle);
                Forms.Application.DoEvents();
                Console.WriteLine("FIXTURE foreground=" + Native.ForegroundEligible(form.Handle));
                Assert(!Native.IsCodex(form.Handle), "process identity rejects title lookalike");
                var layout = ScanWithPump(form.Handle);
                Console.WriteLine("FIXTURE valid=" + layout.Valid + " error=" + layout.Error + " composer=" + layout.Composer + " occupied=" + layout.Occupied.Count + " client=" + layout.Client);
                Assert(layout.Valid && !layout.Composer.IsEmpty, "UIA full composer fixture");
                string reason;
                Rect rect = Placement.Calculate(layout, Native.Client(form.Handle), new MonitorSettings(), out reason);
                Assert(!rect.IsEmpty, "safe gap accepted");
                var state = new QuotaState(); state.Success(Parse(Payload(28, true)), DateTimeOffset.UtcNow);
                IntPtr fixtureForeground = Native.GetForegroundWindow();
                var overlay = new OverlayWindow(new Palette("light"), delegate { });
                overlay.Bar.Update(state); overlay.Place(rect);
                Forms.Application.DoEvents();
                Console.WriteLine("FOCUS before=" + fixtureForeground + " after=" + Native.GetForegroundWindow() + " overlay=" + overlay.Handle);
                Assert(Native.GetForegroundWindow() == fixtureForeground, "overlay preserves foreground");
                Native.RECT native;
                Native.GetWindowRect(overlay.Handle, out native);
                Assert(Math.Abs(native.Top - rect.Top) <= 1 && native.Bottom <= layout.Composer.Top, "native overlay above composer");
                input.Focus(); input.Text = "fixture input remains editable";
                Assert(form.ActiveControl == input && input.Enabled && input.Text.Length > 0, "input stays editable");
                int clicks = 0; send.Click += delegate { clicks++; }; send.PerformClick();
                Assert(clicks == 1, "original send button remains usable");
                form.Left += 75; form.Top += 35;
                Rect moved = Placement.Calculate(layout, Native.Client(form.Handle), new MonitorSettings(), out reason);
                Assert(moved.X == rect.X + 75 && moved.Y == rect.Y + 35, "native window move follows");
                overlay.Place(moved);
                var collision = new Forms.Label { Text = "Visible message fixture", Left = 210, Top = 638, Width = 250, Height = 30 };
                form.Controls.Add(collision);
                var blocked = ScanWithPump(form.Handle);
                var safe = Placement.Calculate(blocked, Native.Client(form.Handle), new MonitorSettings(), out reason);
                Assert(!safe.IsEmpty && !blocked.Occupied.Any(box => box.IntersectsWith(safe)), "UIA text avoided without collision");
                collision.Dispose();
                form.ClientSize = new System.Drawing.Size(700, 850);
                Assert(Placement.Calculate(layout, Native.Client(form.Handle), new MonitorSettings(), out reason).IsEmpty, "native resize hides pending layout");
                form.WindowState = Forms.FormWindowState.Minimized; Forms.Application.DoEvents();
                Assert(!Native.ForegroundEligible(form.Handle), "native minimized hides");
                form.WindowState = Forms.FormWindowState.Normal;
                other.Text = "Foreground fixture"; other.Show(); Native.SetForegroundWindow(other.Handle);
                Forms.Application.DoEvents();
                Assert(!Native.ForegroundEligible(form.Handle), "native other foreground hides");
                overlay.Hide(); overlay.Close();
            }
            if (Native.IsWindow(previous)) Native.SetForegroundWindow(previous);
            results.Add("PASS native fixture: UIA composer, safe above placement, move/resize/minimize/foreground, no focus theft, input/send, chat collision");
        }
        private static void LiveTests()
        {
            string warning; var settings = MonitorSettings.Load(out warning);
            var state = new QuotaState();
            var sync = new object();
            int count = 0, autos = 0; string failure = null;
            using (var service = new RefreshService(settings.CodexExecutable))
            {
                service.Completed += (snapshot, error, automatic) =>
                {
                    lock (sync)
                    {
                        if (error != null) { failure = error; Console.WriteLine("LIVE failure=" + error); }
                        else
                        {
                            state.Success(snapshot, DateTimeOffset.UtcNow); count++;
                            if (automatic) autos++;
                            Console.WriteLine("LIVE read=" + count + " automatic=" + automatic + " fiveRemaining=" +
                                QuotaState.Percent(snapshot.FiveHour) + " weeklyRemaining=" + QuotaState.Percent(snapshot.Weekly));
                        }
                    }
                };
                service.Start();
                PumpUntil(() => { lock (sync) return count >= 1 || failure != null; }, 40000);
                Assert(failure == null && state.Snapshot.FiveHour != null && state.Snapshot.Weekly != null, "real account windows");
                int firstPid = service.Client.ProcessId;
                service.Refresh(false);
                PumpUntil(() => { lock (sync) return count >= 2 || failure != null; }, 25000);
                Assert(failure == null && service.Client.ProcessId == firstPid, "real manual refresh reuses service");
                service.Client.KillForTest(); Thread.Sleep(500);
                service.Refresh(false);
                PumpUntil(() => { lock (sync) return count >= 3 || failure != null; }, 35000);
                Assert(failure == null && service.Client.StartCount == 2, "real child termination recovery");
                var wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 130000)
                {
                    lock (sync) { if (autos >= 2 || failure != null) break; }
                    Forms.Application.DoEvents(); Thread.Sleep(50);
                }
                Assert(failure == null && autos >= 2, "two real 60 second automatic cycles");
                Console.WriteLine(state.Summary(DateTimeOffset.UtcNow));
                RenderPreview(state, "light"); RenderPreview(state, "dark");
                IntPtr codex = Native.FindCodex();
                var layout = codex == IntPtr.Zero ? null : ScanWithPump(codex);
                string reason = "No Codex window";
                Rect placement = layout == null ? Rect.Empty : Placement.Calculate(layout, Native.Client(codex), settings, out reason);
                results.Add("LIVE quota: " + state.Summary(DateTimeOffset.UtcNow).Replace("\n", " | "));
                results.Add("LIVE startup/manual/two 60s cycles/child recovery: passed; starts=" + service.Client.StartCount);
                results.Add("LIVE actual Codex geometry: " + reason + "; placement=" + (placement.IsEmpty ? "hidden" : placement.ToString()));
                results.Add("LIMITATION: actual multi-monitor DPI changes and actual Codex send interaction require manual acceptance; fixture and geometry tests passed.");
            }
        }
        private static void RenderPreview(QuotaState state, string theme)
        {
            var bar = new QuotaBar(new Palette(theme), delegate { });
            bar.Update(state); bar.Measure(new Size(840, 42)); bar.Arrange(new Rect(0, 0, 840, 42)); bar.UpdateLayout();
            var image = new RenderTargetBitmap(1680, 84, 192, 192, PixelFormats.Pbgra32);
            image.Render(bar);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "real-quota-" + theme + ".png");
            using (var stream = File.Create(path)) encoder.Save(stream);
            Assert(bar.ActualWidth == 840 && bar.ActualHeight == 42, "single row measured size");
        }
    }
}
