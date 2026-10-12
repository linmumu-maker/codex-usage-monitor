using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace CodexUsageMonitor
{
    internal sealed class Palette
    {
        public Brush Background, Window, Foreground, Muted, Border, Track, Green, Orange, Red;
        public Brush Input, Hover, Pressed, HoverBorder, Focus, PrimaryHover, PrimaryPressed;
        public bool Dark;
        public Palette(string theme)
        {
            Dark = theme == "dark";
            if (theme == "auto")
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    { Dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0; }
                }
                catch { }
            }
            Background = Brush(Dark ? "#242629" : "#FAFAF9");
            Window = Brush(Dark ? "#1C1E21" : "#F3F4F5");
            Foreground = Brush(Dark ? "#ECEFF2" : "#25292F");
            Muted = Brush(Dark ? "#A7AFB8" : "#66707B");
            Border = Brush(Dark ? "#3A3F45" : "#E4E6E8");
            Track = Brush(Dark ? "#3B4349" : "#E3E8E7");
            Green = Brush(Dark ? "#71C6A6" : "#24785E");
            Orange = Brush(Dark ? "#DDB078" : "#966727");
            Red = Brush(Dark ? "#F1848B" : "#C73F49");
            Input = Brush(Dark ? "#292D32" : "#FFFFFF");
            Hover = Brush(Dark ? "#32383E" : "#F0F2F3");
            Pressed = Brush(Dark ? "#3A424A" : "#E5EAED");
            HoverBorder = Brush(Dark ? "#616B76" : "#BCC4CC");
            Focus = Brush(Dark ? "#81A9CF" : "#537DA4");
            PrimaryHover = Brush(Dark ? "#D4DCE5" : "#3D4652");
            PrimaryPressed = Brush(Dark ? "#BAC6D3" : "#151B22");
        }
        private static Brush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
    }
    internal sealed class QuotaBar : Border
    {
        private readonly TextBlock fivePercent, fiveReset, weekPercent, weekReset, status;
        private readonly Border fiveFill, weekFill;
        private readonly Palette colors;
        private readonly Button refresh;
        public QuotaBar(Palette palette, Action refreshAction)
        {
            colors = palette;
            Resources = UiStyles.Resources(colors);
            Width = Placement.WidthDip; Height = Placement.HeightDip;
            Background = colors.Background; BorderBrush = colors.Border; BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(10); Padding = new Thickness(10, 0, 10, 0);
            SnapsToDevicePixels = true; UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 26 };
            foreach (double width in new[] { 116.0, 12, 274, 12, 274, 32, 98 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            Add(grid, Text("Codex 额度监控", colors.Foreground, 13), 0);
            Add(grid, new Border { Width = 1, Height = 14, Background = colors.Border, Opacity = .65,
                HorizontalAlignment = HorizontalAlignment.Center }, 1);
            Add(grid, WindowRow("5h 剩余", out fivePercent, out fiveReset, out fiveFill), 2);
            // Space replaces the second vertical divider; both quota groups share one baseline.
            Add(grid, WindowRow("周 剩余", out weekPercent, out weekReset, out weekFill), 4);
            refresh = new Button
            {
                Content = new TextBlock { Text = "\uE72C", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16 },
                Style = (Style)Resources["RefreshButton"], Focusable = false, IsTabStop = false,
                ToolTip = "立即刷新真实额度（每 60 秒自动刷新）"
            };
            System.Windows.Automation.AutomationProperties.SetName(refresh, "刷新额度");
            refresh.Click += delegate { refreshAction(); };
            Add(grid, refresh, 5);
            status = Text("正在连接", colors.Muted, 12.5);
            status.Margin = new Thickness(4, 0, 0, 0);
            Add(grid, status, 6);
            Child = grid;
        }
        private TextBlock Text(string value, Brush brush, double size)
        {
            var text = UiStyles.Text(value, brush, size);
            text.TextWrapping = TextWrapping.NoWrap;
            return text;
        }
        private Grid WindowRow(string label, out TextBlock percent, out TextBlock reset, out Border fill)
        {
            var grid = new Grid();
            foreach (double width in new[] { 46.0, 62.0, 42.0, 124.0 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            Add(grid, Text(label, colors.Muted, 12.5), 0);
            percent = Text("暂无数据", colors.Muted, 15);
            percent.FontWeight = FontWeights.SemiBold;
            Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
            Add(grid, percent, 1);
            fill = new Border { Background = colors.Muted, Height = 4, Width = 0, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
            var track = new Border { Width = 38, Height = 4, Background = colors.Track, CornerRadius = new CornerRadius(2), Child = fill,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            Add(grid, track, 2);
            reset = Text("重置 暂无数据", colors.Muted, 12.5);
            Add(grid, reset, 3);
            return grid;
        }
        private static void Add(Grid grid, UIElement element, int column) { Grid.SetColumn(element, column); grid.Children.Add(element); }
        public void Update(QuotaState state)
        {
            var snapshot = state.Snapshot;
            Set(snapshot == null ? null : snapshot.FiveHour, fivePercent, fiveReset, fiveFill, colors.Green);
            Set(snapshot == null ? null : snapshot.Weekly, weekPercent, weekReset, weekFill, colors.Orange);
            bool stale = state.IsStale(DateTimeOffset.UtcNow);
            status.Text = state.Status(DateTimeOffset.UtcNow);
            status.Foreground = state.Error != null || stale ? colors.Red : colors.Muted;
            ToolTip = state.Summary(DateTimeOffset.UtcNow);
            refresh.IsEnabled = !state.Refreshing;
        }
        private void Set(QuotaWindow window, TextBlock percent, TextBlock reset, Border fill, Brush normal)
        {
            double? remaining = window == null ? null : window.Remaining;
            Brush color = !remaining.HasValue ? colors.Muted : remaining.Value < 20 ? colors.Red : normal;
            percent.Text = QuotaState.Percent(window);
            percent.Foreground = color; fill.Background = color;
            fill.Width = remaining.HasValue ? 38 * remaining.Value / 100 : 0;
            reset.Text = "重置 " + QuotaState.Reset(window);
        }
    }
    internal sealed class OverlayWindow : Window
    {
        public readonly QuotaBar Bar;
        internal readonly UsageCard Card;
        public IntPtr Handle { get; private set; }
        public OverlayWindow(Palette colors, Action refresh, string mode = "compactBar", Action hide = null)
        {
            Width = Placement.WidthDip; Height = Placement.HeightDip;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Focusable = false;
            if (mode == "sidebarCard") { Card = new UsageCard(colors, refresh, hide); Width = 360; Height = Card.MeasureHeight(360); Content = Card; }
            else { Bar = new QuotaBar(colors, refresh); Content = Bar; }
            Handle = new WindowInteropHelper(this).EnsureHandle();
            int style = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            HwndSource.FromHwnd(Handle).AddHook(Hook);
        }
        private IntPtr Hook(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); }
            return IntPtr.Zero;
        }
        internal void Update(QuotaState state) { if (Card != null) Card.Update(state); else Bar.Update(state); }
        internal double CardHeight(double width) { return Card.MeasureHeight(width); }
        public void Place(Rect rect)
        {
            if (!IsVisible) Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), (int)Math.Round(rect.X), (int)Math.Round(rect.Y),
                (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
    }
}
