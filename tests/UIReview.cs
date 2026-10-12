using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodexUsageMonitor.Tests
{
    internal static class UIReview
    {
        private static int checks;
        private static int keyboardSkipped;
        private static readonly string Output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
        internal static int Run(bool live)
        {
            Directory.CreateDirectory(Output);
            try
            {
                foreach (string theme in new[] { "light", "dark" })
                {
                    foreach (string scenario in new[] { "normal", "low", "missing", "refreshing", "stale", "failure" })
                    {
                        foreach (double scale in new[] { 1.0, 1.25, 1.5 })
                        {
                            var state = State(scenario);
                            var bar = new QuotaBar(new Palette(theme), delegate { });
                            bar.Update(state); Layout(bar, 840, 42);
                            Check(bar.ActualWidth == 840 && bar.ActualHeight == 42, "single row dimensions");
                            CheckTextFits(bar);
                            foreach (var text in Descendants(bar).OfType<TextBlock>())
                            {
                                if (text.Text.Contains("重置")) Check(text.FontSize == 12.5, "larger reset type");
                                if (text.Text.EndsWith("%")) Check(text.FontSize == 15, "larger percentage type");
                            }
                            var button = Descendants(bar).OfType<Button>().Single();
                            Check(!button.Focusable && !button.IsTabStop, "refresh remains non focusable");
                            if (scenario == "low")
                            {
                                var red = (SolidColorBrush)new Palette(theme).Red;
                                foreach (var text in Descendants(bar).OfType<TextBlock>().Where(t => t.Text.EndsWith("%")))
                                    Check(((SolidColorBrush)text.Foreground).Color == red.Color, "low percent red");
                            }
                            if (scenario == "missing")
                            {
                                var muted = (SolidColorBrush)new Palette(theme).Muted;
                                foreach (var text in Descendants(bar).OfType<TextBlock>().Where(t => t.Text == "暂无数据"))
                                    Check(((SolidColorBrush)text.Foreground).Color == muted.Color, "missing percent neutral");
                            }
                            Export(bar, 840, 42, scale, theme + "-" + scenario + "-" + (int)(scale * 100) + ".png");
                        }
                    }
                    Dialogs(theme);
                }
                if (live)
                {
                    string settingPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "CodexUsageMonitor", "settings.json"));
                    string executable = "";
                    if (File.Exists(settingPath)) executable = Json.Get(Json.Parse(File.ReadAllText(settingPath)), "CodexExecutable") as string ?? "";
                    using (var client = new AppServerClient(executable))
                    {
                        var state = new QuotaState();
                        state.Success(client.Read().GetAwaiter().GetResult(), DateTimeOffset.UtcNow);
                        foreach (string theme in new[] { "light", "dark" })
                        {
                            var bar = new QuotaBar(new Palette(theme), delegate { }); bar.Update(state);
                            Layout(bar, 840, 42); CheckTextFits(bar);
                            Export(bar, 840, 42, 2, "live-" + theme + ".png");
                            var details = new StatusWindow(theme, delegate { }, delegate { });
                            details.Update(state, "自动定位：输入区上方", null);
                            ExportWindow(details, 894, 369, "live-status-" + theme + ".png");
                            details.Close();
                        }
                        Console.WriteLine("UI LIVE " + state.Summary(DateTimeOffset.UtcNow).Replace("\n", " | "));
                    }
                }
                File.WriteAllText(Path.Combine(Output, "review-results.txt"), "PASS UI checks=" + checks +
                    "\n2 themes x 6 states x 3 DPI scales; no text clipping; control templates and long text\n" +
                    "Foreground keyboard paths skipped=" + keyboardSkipped + "; Escape routed action verified\n" +
                    "CheckedLocal=" + DateTimeOffset.Now.ToString("o"));
                Console.WriteLine("PASS UI checks=" + checks);
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL UI " + error.GetType().Name + " " + error.Message + "\n" + error.StackTrace); return 1; }
        }
        private static QuotaState State(string scenario)
        {
            var state = new QuotaState();
            var snapshot = new QuotaSnapshot();
            if (scenario != "missing")
            {
                snapshot.FiveHour = new QuotaWindow { UsedPercent = scenario == "low" ? 81 : 0, ResetsAt = 1893456000 };
                snapshot.Weekly = new QuotaWindow { UsedPercent = scenario == "low" ? 99 : 64.5, ResetsAt = 1894060800 };
            }
            state.Success(snapshot, DateTimeOffset.UtcNow);
            if (scenario == "refreshing") state.Refreshing = true;
            if (scenario == "stale") { state.Failure("测试：查询暂不可用"); state.Refreshing = true; }
            if (scenario == "failure") { state.Clear(); state.Failure("测试：连接不可用"); }
            return state;
        }
        private static void Dialogs(string theme)
        {
            var state = State("stale");
            var details = new StatusWindow(theme, delegate { }, delegate { });
            details.Update(state, "监控状态页在前台；回到 Codex 后自动恢复悬浮栏", "测试配置提示", "最近一次 Codex 前台定位 · 10-09 09:00:00\n与当前可见聊天内容重叠，已安全隐藏。");
            ExportWindow(details, 894, 369, "status-" + theme + ".png");
            CheckTextFits(details.Content as FrameworkElement);
            Check(Descendants(details.Content as DependencyObject).OfType<TextBlock>().Any(t => t.Text == "数据状态"), "data section");
            Check(!Descendants(details.Content as DependencyObject).OfType<TextBlock>().Any(t => t.Text.StartsWith("5h 剩余 ") && t.Text.Contains("\n")), "no duplicate quota summary");
            details.Update(state, new string('长', 280), new string('长', 400));
            Layout(details.Content as FrameworkElement, 894, 369);
            Check(Descendants(details.Content as DependencyObject).OfType<ScrollViewer>().Any(), "long status scroll container");
            details.Close();
            string original = File.Exists(MonitorSettings.FilePath) ? File.ReadAllText(MonitorSettings.FilePath) : null;
            var settings = new MonitorSettings { Theme = theme, CodexExecutable = @"C:\very-long-path\" + new string('x', 160) + @"\codex.exe" };
            var dialog = new SettingsWindow(settings, null, delegate { throw new Exception("unexpected save"); });
            ExportWindow(dialog, 504, 509, "settings-" + theme + ".png");
            var fields = Descendants(dialog.Content as DependencyObject).OfType<TextBox>().ToArray();
            Check(fields.Length == 3 && fields.All(t => t.Height == 34 && t.FontSize == 13), "larger editable inputs");
            foreach (var field in fields) Check(field.Template.FindName("PART_ContentHost", field) != null, "text input host preserved");
            var choices = Descendants(dialog.Content as DependencyObject).OfType<ComboBox>().ToArray();
            Check(choices.Length == 3, "display mode and original dropdowns");
            foreach (var choice in choices)
            {
                choice.SelectedIndex = 1; Layout(choice, choice.ActualWidth, 34);
                var option = (KeyValuePair<string, string>)choice.SelectedItem;
                Check(Descendants(choice).OfType<TextBlock>().Any(t => t.Text == option.Value), "dropdown displays Chinese label");
                Check(!Descendants(choice).OfType<TextBlock>().Any(t => t.Text.StartsWith("[")), "no raw enum pair displayed");
                choice.IsDropDownOpen = true;
                Pump();
                Check(choice.Template.FindName("PART_Popup", choice) != null, "dropdown popup preserved");
                choice.IsDropDownOpen = false;
            }
            var toggle = Descendants(dialog.Content as DependencyObject).OfType<CheckBox>().Single();
            toggle.IsChecked = true; Check(toggle.IsChecked == true, "checkbox toggle");
            var save = Descendants(dialog.Content as DependencyObject).OfType<Button>().First(t => Equals(t.Content, "保存并应用"));
            Check(save.IsDefault && save.Height == 34, "default save button");
            Check(Descendants(dialog.Content as DependencyObject).OfType<Button>().Any(t => t.IsCancel), "cancel keyboard action");
            CheckTextFits(dialog.Content as FrameworkElement);
            foreach (var choice in choices) Check(choice.SelectedValue is string, "display labels preserve string enum keys");
            dialog.Show(); Pump();
            var hwnd = new WindowInteropHelper(dialog).Handle;
            Native.SetForegroundWindow(hwnd); Pump();
            if (Native.GetForegroundWindow() == hwnd)
            {
                fields[0].Focus(); Pump();
                Check(fields[0].IsKeyboardFocused, "textbox keyboard focus");
                fields[0].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); Pump();
                Check(choices[1].IsKeyboardFocusWithin, "Tab enters position dropdown");
                choices[1].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); Pump();
                Check(choices[2].IsKeyboardFocusWithin, "Tab enters theme dropdown");
            }
            else { keyboardSkipped++; Console.WriteLine("UI Tab focus test skipped: native foreground activation refused"); }
            choices[0].IsDropDownOpen = true; Pump();
            var dropdownEscape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            dialog.RaiseEvent(dropdownEscape); Pump();
            Check(dialog.IsVisible, "Escape in display dropdown does not close settings");
            choices[0].IsDropDownOpen = false; Pump();
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, Key.Escape);
            escape.RoutedEvent = Keyboard.PreviewKeyDownEvent;
            dialog.RaiseEvent(escape); Pump();
            Check(!dialog.IsVisible, "Escape closes settings without save");
            dialog.Close();
            Check((File.Exists(MonitorSettings.FilePath) ? File.ReadAllText(MonitorSettings.FilePath) : null) == original, "UI tests do not save configuration");
        }
        private static void Check(bool value, string name)
        {
            if (!value) throw new Exception(name);
            checks++;
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            if (root == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        private static void CheckTextFits(FrameworkElement root)
        {
            foreach (var text in Descendants(root).OfType<TextBlock>())
            {
                if (text.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(text.Text) || text.ActualWidth == 0) continue;
                if (text.TextWrapping == TextWrapping.NoWrap)
                {
                    var font = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
                    var measure = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture,
                        text.FlowDirection, font, text.FontSize, text.Foreground, 1.0);
                    Check(measure.Width <= text.ActualWidth + 1.5, "no horizontal clipping: " + text.Text);
                    Check(measure.Height <= text.ActualHeight + 1.5, "no vertical clipping: " + text.Text);
                }
            }
        }
        private static void Layout(FrameworkElement root, double width, double height)
        {
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        }
        private static void ExportWindow(Window window, double width, double height, string name)
        {
            var content = (FrameworkElement)window.Content;
            window.Content = null;
            var surface = new Border { Background = window.Background, Child = content };
            window.Content = surface;
            Layout(surface, width, height);
            Check(surface.ActualWidth == width && surface.ActualHeight == height, "dialog viewport");
            Export(surface, width, height, 1.25, name);
        }
        private static void Export(FrameworkElement element, double width, double height, double scale, string name)
        {
            Layout(element, width, height);
            var image = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(Output, name))) encoder.Save(stream);
        }
        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
        }
    }
}
