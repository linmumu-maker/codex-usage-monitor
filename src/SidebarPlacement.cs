using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CodexUsageMonitor
{
    internal sealed class SidebarRegion
    {
        public SidebarRegion() { }
        public double Right { get; set; }
        public double Bottom { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        internal void Validate()
        {
            foreach (double value in new[] { Right, Bottom, Width, Height })
                if (Double.IsNaN(value) || Double.IsInfinity(value) || value < 0 || value > 8192) throw new ArgumentException();
            if (Width < 324 || Height < 250) throw new ArgumentException();
        }
        internal Rect Bounds(Rect client, double scale)
        {
            return new Rect(client.Right - (Right + Width) * scale, client.Bottom - (Bottom + Height) * scale, Width * scale, Height * scale);
        }
        internal static SidebarRegion FromSelection(Rect selected, Rect client, double scale)
        {
            if (selected.IsEmpty || !client.Contains(selected) || scale <= 0) throw new ArgumentException();
            var region = new SidebarRegion { Right = (client.Right - selected.Right) / scale, Bottom = (client.Bottom - selected.Bottom) / scale,
                Width = selected.Width / scale, Height = selected.Height / scale };
            region.Validate(); return region;
        }
    }
    internal static class SidebarPlacement
    {
        internal static Rect Region(LayoutSnapshot snapshot, Rect client, MonitorSettings settings, out string reason)
        {
            reason = "";
            if (snapshot == null || !snapshot.Valid || client.IsEmpty) { reason = "右侧布局尚未确定"; return Rect.Empty; }
            if (client.Size != snapshot.Client.Size) { reason = "窗口大小变化，正在重新定位右侧面板"; return Rect.Empty; }
            Vector delta = client.TopLeft - snapshot.Client.TopLeft;
            var panels = snapshot.Sidebars.Select(box => { box.Offset(delta); return box; }).Where(client.Contains).ToList();
            if (panels.Count == 0)
            {
                // Infer a sidebar column only from several distinct aligned structural sections.
                // The editor separation and occupied-content checks below still apply.
                double tolerance = 4 * snapshot.Scale;
                var sections = snapshot.SidebarSections.Select(box => { box.Offset(delta); return box; }).Where(client.Contains).Distinct().ToList();
                foreach (Rect seed in sections)
                {
                    var aligned = sections.Where(box => Math.Abs(box.Left - seed.Left) <= tolerance && Math.Abs(box.Right - seed.Right) <= tolerance)
                        .OrderBy(box => box.Top).ToList();
                    if (aligned.Count < 3) continue;
                    bool separate = true;
                    for (int i = 1; i < aligned.Count; i++)
                        if (aligned[i].Top < aligned[i - 1].Bottom - tolerance || aligned[i].Top - aligned[i - 1].Bottom > 64 * snapshot.Scale) separate = false;
                    double top = aligned[0].Top, bottom = aligned.Max(box => box.Bottom);
                    if (!separate || top > client.Top + client.Height * .4 || bottom - top < 180 * snapshot.Scale) continue;
                    var column = new Rect(aligned.Max(box => box.Left), top, aligned.Min(box => box.Right) - aligned.Max(box => box.Left), client.Bottom - top);
                    if (!panels.Contains(column)) panels.Add(column);
                }
            }
            if (panels.Count == 0) { reason = "未识别到右侧面板结构（空白空间不等于面板边界），可尝试框选校准"; return Rect.Empty; }
            Rect composer = snapshot.Composer;
            if (!composer.IsEmpty) composer.Offset(delta);
            if (settings.SidebarRegion != null)
            {
                Rect calibrated = settings.SidebarRegion.Bounds(client, snapshot.Scale);
                if (!client.Contains(calibrated) || !panels.Any(box => box.Contains(calibrated)))
                { reason = "校准区域已不在右侧面板内，请重新框选"; return Rect.Empty; }
                if (!composer.IsEmpty && composer.IntersectsWith(calibrated))
                { reason = "校准区域侵入输入区，已隐藏"; return Rect.Empty; }
                return calibrated;
            }
            // For a sidebar we only need to establish separation from the main editor.
            // Never treat an edit rectangle as the full composer for compact-bar placement.
            var anchors = composer.IsEmpty ? snapshot.Editors.Where(box => box.Width >= 240 * snapshot.Scale &&
                box.Top > snapshot.Client.Top + snapshot.Client.Height * .5).Select(box => { box.Offset(delta); return box; }).Distinct().ToList() : new List<Rect> { composer };
            if (anchors.Count == 0) { reason = "无法确认右侧面板与输入控件分离，请框选校准"; return Rect.Empty; }
            panels = panels.Where(box => anchors.All(anchor => !box.IntersectsWith(anchor) && box.Left >= anchor.Right - 4 * snapshot.Scale)).ToList();
            var outer = panels.Where(box => !panels.Any(other => other != box && other.Contains(box))).Distinct().ToList();
            if (outer.Count != 1) { reason = "右侧面板边界不明确，请框选校准"; return Rect.Empty; }
            return outer[0];
        }
        internal static Rect Calculate(LayoutSnapshot snapshot, Rect client, MonitorSettings settings, Func<double, double> height, out string reason)
        {
            Rect region = Region(snapshot, client, settings, out reason);
            if (region.IsEmpty) return Rect.Empty;
            double scale = snapshot.Scale, margin = 12 * scale;
            double width = Math.Min(480, region.Width / scale - 24);
            if (width < 300) { reason = "右侧区域太窄，至少需要 324 DIP；卡片已隐藏"; return Rect.Empty; }
            double measured = height(width);
            if (Double.IsNaN(measured) || Double.IsInfinity(measured) || measured <= 0 || region.Height < measured * scale + 24 * scale)
            { reason = "右侧区域高度不足，卡片已隐藏"; return Rect.Empty; }
            var candidate = new Rect(region.Right - margin - width * scale, region.Bottom - margin - measured * scale, width * scale, measured * scale);
            Vector delta = client.TopLeft - snapshot.Client.TopLeft;
            // Try the nearest empty vertical slot inside the identified region, preserving all obstacles.
            for (int attempt = 0; attempt <= snapshot.Occupied.Count; attempt++)
            {
                bool blocked = false; double next = candidate.Top;
                foreach (Rect occupied in snapshot.Occupied)
                {
                    Rect box = occupied; box.Offset(delta);
                    if (!box.IntersectsWith(candidate)) continue;
                    blocked = true; next = Math.Min(next, box.Top - 4 * scale - candidate.Height);
                }
                if (!blocked)
                { reason = settings.SidebarRegion == null ? "自动定位：右侧使用量卡片" : "框选定位：右侧使用量卡片"; return candidate; }
                if (next >= candidate.Top) break;
                candidate.Y = next;
                if (candidate.Top < region.Top + margin) break;
            }
            reason = "右侧区域被原生内容占用，卡片已隐藏"; return Rect.Empty;
        }
    }
}
