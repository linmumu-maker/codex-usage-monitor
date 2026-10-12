using System;
using System.Linq;
using System.Windows;
namespace CodexUsageMonitor.Tests
{
    internal static class PlacementSearchTests
    {
        internal static void Run(Action<bool, string> check)
        {
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                var layout = new LayoutSnapshot { Client = new Rect(-1800, 40, 1200 * scale, 900 * scale),
                    Composer = new Rect(-1700, 40 + 700 * scale, 1000 * scale, 120 * scale), Scale = scale, Valid = true };
                string reason;
                Rect original = Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason);
                var footer = new Rect(original.Left + 250 * scale, layout.Composer.Top - 26 * scale, 300 * scale, 18 * scale);
                layout.Occupied.Add(footer);
                Rect safe = Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason);
                check(!safe.IsEmpty && safe.Bottom <= footer.Top - 4 * scale && reason.Contains("避让"), "footer avoided at DPI " + scale);
                var second = new Rect(original.Left, safe.Top + 5 * scale, 90 * scale, 12 * scale);
                layout.Occupied.Add(second);
                safe = Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason);
                check(!safe.IsEmpty && layout.Occupied.All(x => !x.IntersectsWith(safe)), "chained obstacles avoided at DPI " + scale);
                check(safe.Width == 840 * scale && safe.Height == 42 * scale, "search retains bar dimensions at DPI " + scale);
                var movedClient = layout.Client; movedClient.Offset(150, 80);
                Rect moved = Placement.Calculate(layout, movedClient, new MonitorSettings(), out reason);
                check(moved.Top == safe.Top + 80 && moved.Left == safe.Left + 150, "search translates with window at DPI " + scale);
                var relative = new MonitorSettings { PositionMode = "relative", RelativeCalibrated = true, RelativeBottom = 204 };
                check(Placement.Calculate(layout, layout.Client, relative, out reason).IsEmpty, "relative calibration does not auto relocate at DPI " + scale);
                layout.Occupied.Clear();
                layout.Occupied.Add(new Rect(original.Left, original.Top - 170 * scale, 840 * scale, 210 * scale));
                check(Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason).IsEmpty, "bounded search cannot drift far above at DPI " + scale);
                layout.Occupied.Clear(); layout.Composer.Y = layout.Client.Top + 60 * scale;
                original = Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason);
                layout.Occupied.Add(original);
                check(Placement.Calculate(layout, layout.Client, new MonitorSettings(), out reason).IsEmpty, "search stays inside client at DPI " + scale);
            }
        }
    }
}
