using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor.Tests
{
    internal static class SidebarTests
    {
        private static int checks;
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        internal static void Unit(Action<bool, string> check)
        {
            foreach (string raw in new[] { "3", "0", "3.0" })
                check(QuotaSnapshot.Read(Json.Parse("{\"rateLimitResetCredits\":{\"availableCount\":" + raw + "}}" )).ResetCards.HasValue, "real count format " + raw);
            foreach (string raw in new[] { "-1", "0.5", "\"3\"", "true", "null", "9007199254740992.0" })
                check(!QuotaSnapshot.Read(Json.Parse("{\"rateLimitResetCredits\":{\"availableCount\":" + raw + "}}" )).ResetCards.HasValue, "invalid count missing " + raw);
            check(QuotaSnapshot.Read(Json.Parse("{\"rateLimitResetCredits\":{\"credits\":[{},{}]}}" )).ResetCards == null, "no list-length count inference");
            var creditSample = QuotaSnapshot.Read(Json.Parse("{\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":[{\"status\":\"available\",\"resetType\":\"codexRateLimits\",\"title\":\"Full reset (Weekly + 5 hr)\",\"grantedAt\":1760000000,\"expiresAt\":1760600000},{\"status\":\"available\",\"expiresAt\":null},{\"status\":\"redeemed\",\"expiresAt\":1760600000}]}}"));
            check(creditSample.ResetCards == 3 && creditSample.ResetDetails.Count == 2, "available count independent of filtered credit details");
            check(creditSample.ResetDetails[0].Title == "完全重置（每周 + 5 小时）" && creditSample.ResetDetails[1].Title == "使用限额重置卡", "Chinese credit titles override backend English labels");
            check(creditSample.ResetDetails[0].ValidityText.Contains(Time.LocalReset(1760600000).Value.ToString("yyyy-MM-dd HH:mm")), "credit expiration in Windows local time");
            check(creditSample.ResetDetails[1].NoExpiry && creditSample.ResetDetails[1].ValidityText.Contains("无到期时间"), "explicit null expiry has no deadline");
            var invalidCredit = QuotaSnapshot.Read(Json.Parse("{\"rateLimitResetCredits\":{\"credits\":[{\"status\":\"available\",\"expiresAt\":-1},{\"status\":\"available\",\"expiresAt\":999999999999}]}}"));
            check(invalidCredit.ResetDetails.All(row => row.ValidityText.Contains("到期时间暂无数据")), "invalid and out-of-range expiry unavailable");
            check(QuotaSnapshot.Read(Json.Parse("{}")).ResetDetails == null, "missing details unknown");
            check(QuotaSnapshot.Read(Json.Parse("{\"rateLimitResetCredits\":{\"credits\":[]}}")).ResetDetails.Count == 0, "empty details distinguishable");
            var state = new QuotaState(); var snapshot = new QuotaSnapshot { ResetCards = 3 }; var now = DateTimeOffset.UtcNow;
            state.Success(snapshot, now); state.Failure("offline"); check(state.Snapshot.ResetCards == 3 && state.IsStale(now), "failure preserves stale count");
            state.Success(new QuotaSnapshot { ResetCards = 0 }, now); check(state.Snapshot.ResetCards == 0 && !state.IsStale(now), "count recovery zero fresh");
            state.Clear(); check(state.Snapshot == null, "account change clears count");
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var old = serializer.Deserialize<MonitorSettings>("{\"Theme\":\"dark\",\"RelativeX\":-13}"); old.Validate();
            check(old.DisplayMode == "sidebarCard" && old.RelativeX == -13 && old.SidebarRegion == null, "old config defaults card without replacing offsets");
            bool rejected = false;
            try { SidebarRegion.FromSelection(new Rect(900, 100, 400, 500), new Rect(0, 0, 1100, 900), 1); } catch (ArgumentException) { rejected = true; }
            check(rejected, "selection outside client rejected");
            rejected = false;
            try { new SidebarRegion { Right = Double.NaN, Width = 400, Height = 500 }.Validate(); } catch (ArgumentException) { rejected = true; }
            check(rejected, "nonfinite calibration rejected");
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                var layout = new LayoutSnapshot { Scale = scale, Valid = true, Client = new Rect(-1400, 40, 1500 * scale, 950 * scale),
                    Composer = new Rect(-1300, 40 + 760 * scale, 850 * scale, 110 * scale) };
                var panel = new Rect(layout.Client.Right - 380 * scale, layout.Client.Top + 70 * scale, 376 * scale, 870 * scale);
                layout.Sidebars.Add(panel); string reason;
                Rect card = SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason);
                check(!card.IsEmpty && panel.Contains(card) && card.Width == 352 * scale && card.Height == 320 * scale, "card follows actual panel at DPI " + scale);
                var knownComposer = layout.Composer;
                layout.Composer = Rect.Empty; layout.Editors.Add(knownComposer);
                check(SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason) == card, "sidebar can use editor only for separation at DPI " + scale);
                layout.Editors.Clear();
                check(SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason).IsEmpty, "unconfirmed separation still hides at DPI " + scale);
                layout.Composer = knownComposer;
                var occupied = new Rect(card.Left + 5, card.Top + card.Height - 20 * scale, 200 * scale, 20 * scale);
                layout.Occupied.Add(occupied);
                Rect avoided = SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason);
                check(!avoided.IsEmpty && !avoided.IntersectsWith(occupied), "sidebar avoids bottom content at DPI " + scale);
                layout.Occupied.Add(panel);
                check(SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason).IsEmpty, "occupied sidebar hides at DPI " + scale);
                layout.Occupied.Clear();
                var region = SidebarRegion.FromSelection(panel, layout.Client, scale);
                var settings = new MonitorSettings { SidebarRegion = region };
                settings = serializer.Deserialize<MonitorSettings>(Json.Serialize(settings)); settings.Validate();
                check(settings.SidebarRegion.Bounds(layout.Client, scale) == panel, "calibration and JSON preserve DIP at scale " + scale);
                Rect moved = layout.Client; moved.Offset(170, 45);
                Rect translated = SidebarPlacement.Calculate(layout, moved, settings, width => 320, out reason);
                check(translated.Left == card.Left + 170 && translated.Top == card.Top + 45, "calibration follows moved client at DPI " + scale);
                layout.Sidebars.Clear();
                for (int i = 0; i < 4; i++) layout.SidebarSections.Add(new Rect(panel.Left, panel.Top + i * 65 * scale, panel.Width, 45 * scale));
                check(!SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason).IsEmpty, "aligned sidebar sections inferred at DPI " + scale);
                layout.SidebarSections.RemoveAt(3); layout.SidebarSections.RemoveAt(2);
                check(SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason).IsEmpty, "only two sections cannot establish sidebar at DPI " + scale);
                layout.SidebarSections.Clear();
                check(SidebarPlacement.Calculate(layout, layout.Client, settings, width => 320, out reason).IsEmpty, "collapsed sidebar hides calibrated card at DPI " + scale);
                layout.Sidebars.Add(new Rect(panel.Left, panel.Top, 310 * scale, panel.Height));
                check(SidebarPlacement.Calculate(layout, layout.Client, new MonitorSettings(), width => 320, out reason).IsEmpty, "too narrow sidebar hides at DPI " + scale);
            }
        }
        private static IEnumerable<DependencyObject> Children(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var item in Children(VisualTreeHelper.GetChild(root, i))) yield return item;
        }
        private static QuotaState State(string name)
        {
            var state = new QuotaState();
            var snapshot = new QuotaSnapshot { ResetCards = name == "missing" ? (long?)null : name == "zero" ? 0 : name == "large" ? 2147483647 : 3 };
            if (name != "missing")
            {
                snapshot.FiveHour = new QuotaWindow { UsedPercent = name == "low" ? 91 : 41, ResetsAt = 1893456000L };
                snapshot.Weekly = new QuotaWindow { UsedPercent = name == "low" ? 88 : 6, ResetsAt = 1894060800L };
            }
            state.Success(snapshot, DateTimeOffset.UtcNow.AddSeconds(name == "stale" ? -100 : 0));
            if (name == "refreshing") state.Refreshing = true;
            if (name == "failure") state.Failure("暂时无法连接额度服务");
            return state;
        }
        internal static int Render(bool live)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sidebar-ui"); Directory.CreateDirectory(folder);
            try
            {
                Unit(Check);
                foreach (string theme in new[] { "light", "dark" })
                    foreach (string name in new[] { "normal", "low", "zero", "large", "missing", "refreshing", "stale", "failure" })
                        foreach (double width in new[] { 300.0, 347, 480 })
                            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
                            {
                                var card = new UsageCard(new Palette(theme), delegate { }, delegate { });
                                card.Update(State(name)); double height = card.MeasureHeight(width);
                                card.Arrange(new Rect(0, 0, width, height)); card.UpdateLayout();
                                var toggle = Children(card).OfType<Button>().First(button => Convert.ToString(button.ToolTip).Contains("有效期"));
                                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                double expandedHeight = card.MeasureHeight(width);
                                Check(expandedHeight > height && expandedHeight <= height + 180, "read-only details expansion bounded " + theme + "/" + name + "/" + width + " base=" + height + " expanded=" + expandedHeight);
                                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                Check(card.MeasureHeight(width) == height, "collapse restores placement height");
                                card.Arrange(new Rect(0, 0, width, height)); card.UpdateLayout();
                                Check(height < 460 && height > 240, "content height bounded");
                                Check(Children(card).OfType<Button>().All(button => !button.Focusable && !button.IsTabStop), "actions do not focus");
                                foreach (var text in Children(card).OfType<TextBlock>())
                                {
                                    if (text.ActualWidth == 0) continue;
                                    var measured = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture, text.FlowDirection,
                                        new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, 1);
                                    measured.MaxTextWidth = Math.Max(1, text.ActualWidth);
                                    Check(measured.Height <= text.ActualHeight + 2, "card text not vertically clipped " + theme + "/" + name + "/" + width + " " + text.Text + " actual=" + text.ActualWidth + "x" + text.ActualHeight + " measuredHeight=" + measured.Height);
                                    Check(text.ActualWidth <= width, "text remains inside card");
                                }
                                Check(!Children(card).OfType<Button>().Any(button => Convert.ToString(button.Content).Contains("重置")), "no consume reset button");
                                Export(card, width, height, scale, Path.Combine(folder, theme + "-" + name + "-" + width + "-" + scale + ".png"));
                            }
                if (live)
                {
                    using (var client = new AppServerClient(""))
                    {

            var state = new QuotaState(); state.Success(client.Read().GetAwaiter().GetResult(), DateTimeOffset.UtcNow);
                        foreach (string theme in new[] { "light", "dark" })
                        {
                            var card = new UsageCard(new Palette(theme), delegate { }, delegate { }); card.Update(state);
                            double height = card.MeasureHeight(347); Export(card, 347, height, 1.5, Path.Combine(folder, "live-" + theme + ".png"));
                        }
                        Console.WriteLine("SIDEBAR LIVE " + state.Summary(DateTimeOffset.UtcNow).Replace("\n", " | "));
                    }
                }
                string report = "PASS sidebar checks=" + checks + "\n2 themes x 8 states x 3 widths x 3 DPI; read-only reset count; CheckedLocal=" + DateTimeOffset.Now.ToString("o");
                File.WriteAllText(Path.Combine(folder, "results.txt"), report); Console.WriteLine(report); return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL sidebar " + error.Message + "\n" + error.StackTrace); return 1; }
        }
        private static void Export(UsageCard card, double width, double height, double scale, string path)
        {
            card.Measure(new Size(width, height)); card.Arrange(new Rect(0, 0, width, height)); card.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render(card); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
