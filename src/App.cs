using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (Array.IndexOf(args, "--enable-autostart") >= 0 || Array.IndexOf(args, "--disable-autostart") >= 0)
            {
                try { StartupRegistration.Set(Array.IndexOf(args, "--enable-autostart") >= 0); }
                catch { Environment.ExitCode = 1; }
                return;
            }
            bool background = Array.IndexOf(args, "--background") >= 0;
            bool created;
            using (var mutex = new Mutex(true, @"Local\CodexUsageMonitor.Wpf", out created))
            {
                if (!created) { if (!background) MessageBox.Show("Codex 额度监控已在运行，请查看系统托盘。"); return; }
                try
                {
                    var application = new MonitorApplication(Array.IndexOf(args, "--show-status") >= 0, null, null, background);
                    application.Run();
                }
                catch { MessageBox.Show("额度监控启动失败。请确认 Windows 10/11 与 .NET Framework 4.8 可用。", "Codex 额度监控"); }
            }
        }
    }
    internal sealed class MonitorApplication : Application
    {
        private readonly QuotaState state = new QuotaState();
        private readonly PlacementHistory placementHistory = new PlacementHistory();
        private readonly WindowTracker tracker;
        private readonly Forms.NotifyIcon tray;
        private readonly DispatcherTimer follower;
        private readonly ForegroundWatch foregroundWatch;
        private MonitorSettings settings;
        private RefreshService service;
        private OverlayWindow overlay;
        private StatusWindow details;
        private Window settingsWindow;
        private bool evaluationQueued, stopping, cardHidden;
        private string positionReason = "正在定位输入区";
        private string configWarning;
        private readonly System.Drawing.Icon trayIcon;
        private readonly bool waitForCodex;

        public MonitorApplication(bool showStatus) : this(showStatus, null, null) { }

        // Internal dependencies let lifecycle tests use their own window and RPC fixture.
        // Production always discovers the real Codex window and loads the existing settings.
        internal MonitorApplication(bool showStatus, MonitorSettings initialSettings, WindowTracker initialTracker, bool background = false)
        {
            waitForCodex = background;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            settings = initialSettings ?? MonitorSettings.Load(out configWarning);
            overlay = new OverlayWindow(new Palette(settings.Theme), Refresh, settings.DisplayMode, HideCard);
            MainWindow = overlay;
            tracker = initialTracker ?? new WindowTracker();
            foregroundWatch = new ForegroundWatch(() => RequestOverlayEvaluation(true));
            trayIcon = (System.Drawing.Icon)System.Drawing.SystemIcons.Information.Clone();
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Codex 额度监控", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("查看额度与定位状态", null, delegate { ShowDetails(); });
            menu.Items.Add("立即刷新", null, delegate { Refresh(); });
            menu.Items.Add("显示使用量卡片", null, delegate { SetDisplayMode("sidebarCard"); });
            menu.Items.Add("显示原单行栏", null, delegate { SetDisplayMode("compactBar"); });
            menu.Items.Add("右侧区域框选 / 校准", null, delegate { CalibrateSidebar(); });
            menu.Items.Add("位置与外观设置", null, delegate { ShowSettings(); });
            var startup = new Forms.ToolStripMenuItem("登录 Windows 后待命，跟随 Codex 启动");
            menu.Opening += delegate
            {
                try { startup.Checked = StartupRegistration.Enabled; startup.Enabled = true; }
                catch { startup.Checked = false; startup.Enabled = false; }
            };
            startup.Click += delegate
            {
                try { StartupRegistration.Set(!StartupRegistration.Enabled); }
                catch { MessageBox.Show("自动启动设置失败，请检查当前用户的 Windows 启动项权限。", "Codex 额度监控"); }
            };
            menu.Items.Add(startup);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Shutdown(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowDetails(); };
            follower = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            follower.Tick += delegate { EvaluateOverlay(); };
            follower.Start();
            if (!waitForCodex) StartService();
            if (showStatus) Dispatcher.BeginInvoke(new Action(ShowDetails));
        }
        private void Ui(Action work)
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(work);
        }
        private void StartService()
        {
            service = new RefreshService(settings.CodexExecutable);
            RefreshService current = service;
            current.Started += () => Ui(() => { if (service != current) return; state.Refreshing = true; Render(); });
            current.AccountChanged += () => Ui(() => { if (service != current) return; state.Clear(); Render(); });
            current.Completed += (snapshot, error, automatic) => Ui(() =>
            {
                if (service != current) return;
                state.Refreshing = false;
                if (error == null) state.Success(snapshot, DateTimeOffset.UtcNow);
                else state.Failure(error);
                Render();
            });
            current.Start();
        }
        private void Refresh() { if (service == null) StartService(); else service.Refresh(false); }
        private void Render()
        {
            overlay.Update(state);
            string percent = "5h " + QuotaState.Percent(state.Snapshot == null ? null : state.Snapshot.FiveHour) +
                " / 周 " + QuotaState.Percent(state.Snapshot == null ? null : state.Snapshot.Weekly);
            string text = "Codex 剩余 " + percent + (state.IsStale(DateTimeOffset.UtcNow) ? " · 已过期" : "");
            tray.Text = text.Length <= 63 ? text : text.Substring(0, 63);
            if (details != null) details.Update(state, positionReason, configWarning, placementHistory.Description);
        }
        private void RequestOverlayEvaluation(bool rescan)
        {
            if (Dispatcher.HasShutdownStarted || stopping || tray == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Ui(() => RequestOverlayEvaluation(rescan));
                return;
            }
            if (rescan) tracker.RequestScan();
            if (evaluationQueued) return;
            evaluationQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                evaluationQueued = false;
                if (!Dispatcher.HasShutdownStarted && !stopping) EvaluateOverlay();
            }));
        }
        private void EvaluateOverlay()
        {
            IntPtr target;
            string reason;
            IntPtr statusHandle = details == null ? IntPtr.Zero : new WindowInteropHelper(details).Handle;
            Rect position = settings.DisplayMode == "sidebarCard" ? tracker.PositionSidebar(settings, statusHandle, overlay.CardHeight, out reason, out target) :
                tracker.Position(settings, statusHandle, out reason, out target);
            if (settings.DisplayMode == "sidebarCard" && cardHidden) { position = Rect.Empty; reason = "使用量卡片已手动隐藏，可从托盘重新显示"; }
            if (waitForCodex && service == null && target != IntPtr.Zero && Native.IsWindow(target)) StartService();
            positionReason = service == null ? "后台待命：检测到 Codex 后自动读取额度" : reason;
            placementHistory.Observe(target, Native.ForegroundEligible(target), reason, DateTimeOffset.UtcNow);
            if (position.IsEmpty) { if (overlay.IsVisible) overlay.Hide(); }
            else overlay.Place(position);
            Render();
        }
        private void ShowDetails()
        {
            if (details != null)
            {
                if (details.WindowState == WindowState.Minimized) details.WindowState = WindowState.Normal;
                if (!details.IsVisible) details.Show();
                details.Activate();
                RequestOverlayEvaluation(true);
                return;
            }
            details = new StatusWindow(settings.Theme, Refresh, ShowSettings);
            details.StateChanged += delegate { RequestOverlayEvaluation(true); };
            details.Closed += delegate { details = null; RequestOverlayEvaluation(true); };
            Render(); details.Show();
            RequestOverlayEvaluation(true);
        }
        private void ShowSettings()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            settingsWindow = new SettingsWindow(settings, tracker.Latest, ApplySettings, CalibrateSidebar);
            settingsWindow.Closed += delegate { settingsWindow = null; RequestOverlayEvaluation(true); };
            settingsWindow.Show();
        }
        private void HideCard() { cardHidden = true; RequestOverlayEvaluation(false); }
        private void SetDisplayMode(string mode)
        {
            var next = CloneSettings(); next.DisplayMode = mode;
            try { next.Save(); cardHidden = false; ApplySettings(next); }
            catch { MessageBox.Show("无法保存显示模式。请检查运行目录的写入权限。", "Codex 额度监控"); }
        }
        private MonitorSettings CloneSettings()
        {
            return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<MonitorSettings>(Json.Serialize(settings));
        }
        private void CalibrateSidebar()
        {
            var latest = tracker.Latest;
            if (latest == null || !Native.IsWindow(latest.Window) || Native.IsIconic(latest.Window))
            { MessageBox.Show("请先打开 Codex 和右侧面板，稍后再框选。", "Codex 额度监控"); return; }
            if (settingsWindow != null) settingsWindow.Close();
            overlay.Hide();
            try
            {
                settingsWindow = new SidebarCalibration(latest.Window, region =>
                {
                    var next = CloneSettings(); next.DisplayMode = "sidebarCard"; next.SidebarRegion = region;
                    next.Save(); cardHidden = false; ApplySettings(next);
                });
                settingsWindow.Closed += delegate { settingsWindow = null; RequestOverlayEvaluation(true); };
                settingsWindow.Show();
            }
            catch { MessageBox.Show("无法打开区域框选，请确认 Codex 未最小化。", "Codex 额度监控"); }
        }
        private void ApplySettings(MonitorSettings next)
        {
            bool restart = settings.CodexExecutable != next.CodexExecutable;
            settings = next; configWarning = null; cardHidden = false;
            placementHistory.Clear();
            overlay.Close(); overlay = new OverlayWindow(new Palette(settings.Theme), Refresh, settings.DisplayMode, HideCard);
            MainWindow = overlay;
            if (details != null) details.Close();
            if (restart) { if (service != null) service.Dispose(); service = null; state.Clear(); if (!waitForCodex) StartService(); }
            RequestOverlayEvaluation(true);
            Render();
        }
        protected override void OnExit(ExitEventArgs e)
        {
            stopping = true;
            follower.Stop(); foregroundWatch.Dispose(); tracker.Dispose(); if (service != null) service.Dispose();
            tray.Visible = false; tray.Dispose(); trayIcon.Dispose();
            overlay.Close();
            base.OnExit(e);
        }
    }
}
