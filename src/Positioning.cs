using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace CodexUsageMonitor
{
    internal sealed class LayoutSnapshot
    {
        public IntPtr Window;
        public Rect Client;
        public Rect Composer = Rect.Empty;
        public readonly List<Rect> Occupied = new List<Rect>();
        public readonly List<Rect> Editors = new List<Rect>();
        public readonly List<Rect> Sidebars = new List<Rect>();
        public readonly List<Rect> SidebarSections = new List<Rect>();
        public DateTimeOffset Captured;
        public bool Valid;
        public string Error;
        public double Scale;
    }
    internal static class LayoutDetector
    {
        private static bool Usable(Rect rect) { return !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && !double.IsNaN(rect.X) && !double.IsInfinity(rect.X); }
        internal static LayoutSnapshot Scan(IntPtr window)
        {
            var snapshot = new LayoutSnapshot { Window = window, Client = Native.Client(window), Captured = DateTimeOffset.UtcNow, Scale = Native.Scale(window) };
            try
            {
                if (snapshot.Client.IsEmpty || Native.IsIconic(window)) { snapshot.Error = "Codex 已最小化"; return snapshot; }
                var root = AutomationElement.FromHandle(window);
                var cache = new CacheRequest();
                cache.Add(AutomationElement.BoundingRectangleProperty);
                cache.Add(AutomationElement.ControlTypeProperty);
                cache.Add(AutomationElement.IsOffscreenProperty);
                AutomationElementCollection all;
                using (cache.Activate()) all = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                var composers = new List<Rect>();
                foreach (AutomationElement element in all)
                {
                    var current = element.Cached;
                    Rect rect = current.BoundingRectangle;
                    if (current.IsOffscreen || !Usable(rect)) continue;
                    ControlType type = current.ControlType;
                    if ((type == ControlType.Group || type == ControlType.Pane) && rect.Width >= 280 * snapshot.Scale &&
                        rect.Width <= 650 * snapshot.Scale && rect.Height >= snapshot.Client.Height * .4 &&
                        rect.Right >= snapshot.Client.Right - 24 * snapshot.Scale && snapshot.Client.Contains(rect) &&
                        rect.Width < snapshot.Client.Width * .65 && !snapshot.Sidebars.Contains(rect)) snapshot.Sidebars.Add(rect);
                    // Some Codex layouts expose only aligned sidebar sections, not its outer panel.
                    if (type == ControlType.Group && rect.Width >= 324 * snapshot.Scale && rect.Width <= 650 * snapshot.Scale &&
                        rect.Height >= 24 * snapshot.Scale && rect.Right >= snapshot.Client.Right - 24 * snapshot.Scale &&
                        snapshot.Client.Contains(rect) && rect.Width < snapshot.Client.Width * .45 &&
                        !snapshot.SidebarSections.Contains(rect)) snapshot.SidebarSections.Add(rect);
                    if (type == ControlType.Edit) snapshot.Editors.Add(rect);
                    if (type == ControlType.Edit && rect.Width >= 240 * snapshot.Scale &&
                        rect.Height >= 16 * snapshot.Scale && rect.Top > snapshot.Client.Top + snapshot.Client.Height * .5)
                    {
                        AutomationElement ancestor = TreeWalker.ControlViewWalker.GetParent(element);
                        for (int depth = 0; ancestor != null && depth < 4; depth++)
                        {
                            var parent = ancestor.Current;
                            Rect box = parent.BoundingRectangle;
                            if (parent.ControlType == ControlType.Group && Usable(box) && box.Contains(rect) &&
                                box.Height <= Math.Min(360 * snapshot.Scale, snapshot.Client.Height * .45) && box.Width >= rect.Width)
                            { if (!composers.Contains(box)) composers.Add(box); break; }
                            ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor);
                        }
                    }
                    // Structural bounding rectangles only. Never read Name, Value, TextPattern or chat content.
                    bool interactive = type == ControlType.Button || type == ControlType.Edit || type == ControlType.CheckBox ||
                        type == ControlType.ComboBox || type == ControlType.Slider || type == ControlType.Hyperlink ||
                        type == ControlType.MenuItem || type == ControlType.TabItem || type == ControlType.ListItem || type == ControlType.ScrollBar;
                    bool content = type == ControlType.Text || type == ControlType.Image || type == ControlType.DataItem;
                    if (interactive || content) snapshot.Occupied.Add(rect);
                }
                if (composers.Count == 1) snapshot.Composer = composers[0];
                else snapshot.Error = composers.Count > 1 ? "输入区定位存在歧义，请校准" : "无法识别完整输入区，请校准";
                // Reject snapshots captured during a move/resize.
                if (Native.Client(window) != snapshot.Client) { snapshot.Error = "窗口布局正在变化"; return snapshot; }
                snapshot.Captured = DateTimeOffset.UtcNow;
                snapshot.Valid = true;
            }
            catch { snapshot.Error = "无法读取可访问性布局，请校准"; }
            return snapshot;
        }
    }
    internal static class Placement
    {
        public const double WidthDip = 840, HeightDip = 42, GapDip = 4, SearchUpDip = 160;
        internal static Rect Calculate(LayoutSnapshot snapshot, Rect client, MonitorSettings settings, out string reason)
        {
            reason = "";
            if (snapshot == null || !snapshot.Valid || client.IsEmpty) { reason = snapshot == null ? "正在定位输入区" : snapshot.Error; return Rect.Empty; }
            if (client.Size != snapshot.Client.Size) { reason = "窗口大小变化，正在重新定位"; return Rect.Empty; }
            double scale = snapshot.Scale;
            double width = WidthDip * scale, height = HeightDip * scale, gap = GapDip * scale;
            if (client.Width < width + 8 * scale) { reason = "窗口太窄，单行额度栏已隐藏"; return Rect.Empty; }
            Vector delta = client.TopLeft - snapshot.Client.TopLeft;
            Rect composer = snapshot.Composer;
            if (!composer.IsEmpty) composer.Offset(delta);
            Rect candidate;
            if (settings.PositionMode == "relative" || composer.IsEmpty)
            {
                if (!settings.RelativeCalibrated) { reason = snapshot.Error ?? "请先校准相对位置"; return Rect.Empty; }
                candidate = new Rect(client.Left + (client.Width - width) / 2 + settings.RelativeX * scale,
                    client.Bottom - settings.RelativeBottom * scale - height, width, height);
            }
            else candidate = new Rect(composer.Left + (composer.Width - width) / 2, composer.Top - gap - height, width, height);
            if (!client.Contains(candidate)) { reason = "输入区上方空间不足，额度栏已隐藏"; return Rect.Empty; }
            if (!composer.IsEmpty && candidate.Bottom > composer.Top - gap + .5)
            { reason = "位置侵入输入区，额度栏已隐藏"; return Rect.Empty; }
            if (composer.IsEmpty)
            {
                // Relative mode still requires an identified editable area below the candidate.
                // The occupied geometry is checked below; calibration never disables collision checks.
                bool aboveEditor = snapshot.Editors.Any(editor => editor.Top + delta.Y >= candidate.Bottom + gap &&
                    editor.Bottom + delta.Y <= client.Bottom && editor.Width >= 240 * scale &&
                    editor.Top + delta.Y > client.Top + client.Height * .5 &&
                    editor.Left + delta.X < candidate.Right && editor.Right + delta.X > candidate.Left);
                if (!aboveEditor) { reason = "无法确认相对位置位于输入框上方，已隐藏"; return Rect.Empty; }
            }
            bool automatic = settings.PositionMode != "relative" && !composer.IsEmpty;
            double initialTop = candidate.Top;
            // Move above each intersecting obstacle; never skip occupied content or buttons.
            // The position moves strictly upward and is bounded near the composer.
            for (int attempt = 0; attempt <= snapshot.Occupied.Count; attempt++)
            {
                double nextTop = candidate.Top;
                bool blocked = false;
                foreach (Rect occupied in snapshot.Occupied)
                {
                    Rect rect = occupied; rect.Offset(delta);
                    if (!rect.IntersectsWith(candidate)) continue;
                    blocked = true;
                    nextTop = Math.Min(nextTop, rect.Top - gap - height);
                }
                if (!blocked)
                {
                    reason = automatic ? (candidate.Top < initialTop ? "自动定位：已避让上方控件" : "自动定位：输入区上方") : "相对定位：输入区上方";
                    return candidate;
                }
                if (!automatic || nextTop >= candidate.Top || initialTop - nextTop > SearchUpDip * scale) break;
                candidate.Y = nextTop;
                if (!client.Contains(candidate)) break;
            }
            reason = automatic ? "输入区上方附近没有足够安全空白，已隐藏；可打开托盘校准" : "校准位置与聊天内容或按钮重叠，已隐藏；可打开托盘校准";
            return Rect.Empty;
        }
    }
    internal sealed class WindowTracker : IDisposable
    {
        private readonly object sync = new object();
        private readonly Timer scanner;
        private LayoutSnapshot snapshot;
        private int scanning;
        private bool disposed;
        private readonly Func<IntPtr> findWindow;
        public WindowTracker() : this(Native.FindCodex) { }
        internal WindowTracker(Func<IntPtr> finder)
        {
            findWindow = finder ?? Native.FindCodex;
            scanner = new Timer(_ => Scan(), null, 0, 800);
        }
        public void RequestScan()
        {
            if (disposed) return;
            try { scanner.Change(0, 800); }
            catch (ObjectDisposedException) { } // Disposal can race a queued foreground notification.
        }
        private void Scan()
        {
            if (disposed || Interlocked.CompareExchange(ref scanning, 1, 0) != 0) return;
            try
            {
                IntPtr target = findWindow();
                var next = target == IntPtr.Zero ? null : LayoutDetector.Scan(target);
                lock (sync) { if (!disposed) snapshot = next; }
            }
            finally { Interlocked.Exchange(ref scanning, 0); }
        }
        public LayoutSnapshot Latest { get { lock (sync) return snapshot; } }
        public Rect Position(MonitorSettings settings, out string reason, out IntPtr target)
        {
            return Position(settings, IntPtr.Zero, out reason, out target);
        }
        public Rect Position(MonitorSettings settings, IntPtr statusWindow, out string reason, out IntPtr target)
        {
            var current = Latest;
            target = current == null ? IntPtr.Zero : current.Window;
            IntPtr foreground = Native.GetForegroundWindow();
            bool exists = target != IntPtr.Zero && Native.IsWindow(target);
            reason = OverlayVisibility.HiddenReason(exists, exists && Native.IsIconic(target),
                exists && Native.IsWindowVisible(target), exists && foreground == target,
                statusWindow != IntPtr.Zero && foreground == statusWindow);
            if (reason != null) return Rect.Empty;
            if (DateTimeOffset.UtcNow - current.Captured > TimeSpan.FromSeconds(2))
            { reason = "布局检测未完成，已安全隐藏"; return Rect.Empty; }
            if (Math.Abs(Native.Scale(target) - current.Scale) > .01)
            { reason = "DPI 变化，正在重新定位"; return Rect.Empty; }
            return Placement.Calculate(current, Native.Client(target), settings, out reason);
        }
        internal Rect PositionSidebar(MonitorSettings settings, IntPtr statusWindow, Func<double, double> measureHeight, out string reason, out IntPtr target)
        {
            var current = Latest; target = current == null ? IntPtr.Zero : current.Window;
            IntPtr foreground = Native.GetForegroundWindow();
            bool exists = target != IntPtr.Zero && Native.IsWindow(target);
            reason = OverlayVisibility.HiddenReason(exists, exists && Native.IsIconic(target), exists && Native.IsWindowVisible(target),
                exists && foreground == target, statusWindow != IntPtr.Zero && foreground == statusWindow);
            if (reason != null) return Rect.Empty;
            if (DateTimeOffset.UtcNow - current.Captured > TimeSpan.FromSeconds(2)) { reason = "右侧布局检测未完成，已安全隐藏"; return Rect.Empty; }
            if (Math.Abs(Native.Scale(target) - current.Scale) > .01) { reason = "DPI 变化，正在重新定位右侧面板"; return Rect.Empty; }
            return SidebarPlacement.Calculate(current, Native.Client(target), settings, measureHeight, out reason);
        }
        public void Dispose() { disposed = true; scanner.Dispose(); }
    }
}
