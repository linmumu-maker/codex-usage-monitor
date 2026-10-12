using System;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CodexUsageMonitor
{
    internal sealed class StatusWindow : Window
    {
        public readonly QuotaBar Bar;
        private readonly Palette colors;
        private readonly TextBlock dataStatus, lastUpdate, dataExplanation, placement, placementHistory, warning;
        public StatusWindow(string theme, Action refresh, Action calibrate)
        {
            colors = new Palette(theme);
            UiStyles.Apply(this, colors);
            Title = "Codex 额度监控 · 状态";
            Width = 910; Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanMinimize;
            var root = new Grid { Margin = new Thickness(24) };
            for (int i = 0; i < 2; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var identity = new StackPanel();
            var title = UiStyles.Text("Codex 额度监控", colors.Foreground, 16);
            title.FontWeight = FontWeights.SemiBold;
            identity.Children.Add(title);
            identity.Children.Add(UiStyles.Text("实时账户额度 · 每 60 秒自动刷新", colors.Muted, 12.5));
            header.Children.Add(identity);
            var update = new Button { Content = "立即刷新", Width = 108, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            update.Click += delegate { refresh(); }; Grid.SetColumn(update, 1); header.Children.Add(update);
            root.Children.Add(header);
            Bar = new QuotaBar(colors, refresh) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
            Grid.SetRow(Bar, 1); root.Children.Add(Bar);
            var cards = new Grid();
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var data = new StackPanel();
            data.Children.Add(SectionTitle("数据状态"));
            dataStatus = UiStyles.Text("正在连接", colors.Foreground, 13);
            dataStatus.FontWeight = FontWeights.SemiBold; dataStatus.Margin = new Thickness(0, 0, 0, 7);
            data.Children.Add(dataStatus);
            lastUpdate = UiStyles.Text("尚未成功读取", colors.Muted, 12.5);
            data.Children.Add(lastUpdate);
            dataExplanation = UiStyles.Text("", colors.Muted, 13);
            dataExplanation.Margin = new Thickness(0, 7, 0, 0);
            data.Children.Add(dataExplanation);
            cards.Children.Add(UiStyles.Card(colors, data));
            var location = new StackPanel();
            location.Children.Add(SectionTitle("定位状态"));
            placement = UiStyles.Text("", colors.Foreground, 13);
            placement.Margin = new Thickness(0, 0, 0, 10); location.Children.Add(placement);
            placementHistory = UiStyles.Text("", colors.Muted, 12.5);
            placementHistory.Margin = new Thickness(0, 0, 0, 10);
            placementHistory.Visibility = Visibility.Collapsed;
            location.Children.Add(placementHistory);
            warning = UiStyles.Text("", colors.Red, 12.5);
            warning.Margin = new Thickness(0, 0, 0, 10); location.Children.Add(warning);
            var positionButton = new Button { Content = "定位 / 校准", Width = 112, HorizontalAlignment = HorizontalAlignment.Left };
            positionButton.Click += delegate { calibrate(); }; location.Children.Add(positionButton);
            var positionCard = UiStyles.Card(colors, location);
            Grid.SetColumn(positionCard, 2); cards.Children.Add(positionCard);
            var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 2); root.Children.Add(scroll);
            Content = root;
        }
        private TextBlock SectionTitle(string value)
        {
            var title = UiStyles.Text(value, colors.Foreground, 15);
            title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 12);
            return title;
        }
        public void Update(QuotaState state, string positionReason, string configWarning, string lastPlacement = null)
        {
            Bar.Update(state);
            var now = DateTimeOffset.UtcNow;
            bool stale = state.IsStale(now);
            dataStatus.Text = state.Status(now);
            dataStatus.Foreground = stale || state.Error != null ? colors.Red : colors.Foreground;
            lastUpdate.Text = state.LastSuccess.HasValue ?
                "最后更新 " + state.LastSuccess.Value.ToLocalTime().ToString("MM-dd HH:mm:ss") : "尚未成功读取";
            bool missing = state.Snapshot != null && state.Snapshot.FiveHour == null && state.Snapshot.Weekly == null;
            dataExplanation.Text = state.Error ?? (stale ? "当前显示上次成功读取的数据，等待下一次刷新。" :
                missing ? "当前账号暂未返回可用的额度窗口。" : "百分比为剩余额度，重置时间使用 Windows 本地时间。");
            dataExplanation.Foreground = state.Error == null ? colors.Muted : colors.Red;
            dataExplanation.Text += "\n重置卡：" + (state.Snapshot != null && state.Snapshot.ResetCards.HasValue ? "可用 " + state.Snapshot.ResetCards.Value + " 张" : "暂无数据");
            placement.Text = positionReason ?? "正在定位输入区";
            placementHistory.Text = lastPlacement ?? "";
            placementHistory.Visibility = string.IsNullOrEmpty(lastPlacement) ? Visibility.Collapsed : Visibility.Visible;
            warning.Text = configWarning ?? "";
            warning.Visibility = configWarning == null ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    internal sealed class SettingsWindow : Window
    {
        private readonly TextBox executable, horizontal, bottom;
        private readonly ComboBox mode, theme, display;
        private readonly CheckBox calibrated;
        public SettingsWindow(MonitorSettings settings, LayoutSnapshot layout, Action<MonitorSettings> apply, Action selectRegion = null)
        {
            var colors = new Palette(settings.Theme);
            UiStyles.Apply(this, colors);
            Title = "Codex 额度监控 · 位置校准";
            Width = 520; Height = 540; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            var title = UiStyles.Text("位置与外观", colors.Foreground, 15);
            title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
            header.Children.Add(UiStyles.Text("右侧使用量卡片或输入区单行栏，均保留遮挡检查。", colors.Muted, 12.5));
            root.Children.Add(header);
            var form = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            display = Choice(form, "显示模式", new[] { "sidebarCard", "compactBar" }, settings.DisplayMode, colors);
            display.Margin = new Thickness(0, 0, 0, 12);
            if (selectRegion != null)
            {
                var regionButton = new Button { Content = "框选右侧卡片区域", Margin = new Thickness(0, 0, 0, 12) };
                regionButton.Click += delegate { selectRegion(); }; form.Children.Add(regionButton);
            }
            executable = Input(form, "Codex CLI 路径", settings.CodexExecutable, colors);
            executable.ToolTip = "空白时自动发现；指定时须使用 codex.exe 完整路径。";
            var choices = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel(); var right = new StackPanel();
            mode = Choice(left, "定位方式", new[] { "auto", "relative" }, settings.PositionMode, colors);
            mode.ToolTip = "auto 自动定位；relative 使用已确认的相对位置。";
            theme = Choice(right, "外观主题", new[] { "auto", "light", "dark" }, settings.Theme, colors);
            theme.ToolTip = "auto 跟随 Windows 应用主题；light 浅色；dark 深色。";
            choices.Children.Add(left); Grid.SetColumn(right, 2); choices.Children.Add(right); form.Children.Add(choices);
            double x = settings.RelativeX, y = settings.RelativeBottom;
            if (layout != null && layout.Valid && !layout.Composer.IsEmpty)
            {
                x = (layout.Composer.Left + layout.Composer.Width / 2 - layout.Client.Left - layout.Client.Width / 2) / layout.Scale;
                y = (layout.Client.Bottom - layout.Composer.Top) / layout.Scale + Placement.GapDip;
            }
            horizontal = Input(form, "原单行栏横向偏移 DIP · 相对客户区中心，向右为正", x.ToString("0.#", CultureInfo.InvariantCulture), colors);
            bottom = Input(form, "原单行栏底部距离 DIP · 额度栏底部到客户区底部", y.ToString("0.#", CultureInfo.InvariantCulture), colors);
            calibrated = new CheckBox
            {
                Content = new TextBlock { Text = "已确认相对位置，允许自动识别失败时使用", TextWrapping = TextWrapping.Wrap },
                IsChecked = settings.RelativeCalibrated, Margin = new Thickness(0, 4, 0, 8)
            };
            form.Children.Add(calibrated);
            form.Children.Add(UiStyles.Text("调整偏移不能创造空白；与聊天内容或按钮重叠时仍会隐藏。", colors.Muted, 12.5));
            var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = new Button { Content = "取消", Width = 82, IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
            cancel.Click += delegate { Close(); }; actions.Children.Add(cancel);
            var save = new Button { Content = "保存并应用", Width = 124, Style = (Style)Resources["PrimaryButton"], IsDefault = true };
            save.Click += delegate
            {
                double nextX, nextBottom;
                if (!double.TryParse(horizontal.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out nextX) ||
                    !double.TryParse(bottom.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out nextBottom))
                { MessageBox.Show(this, "偏移请输入数字（小数点使用 .）。"); return; }
                var next = new MonitorSettings { CodexExecutable = executable.Text.Trim(), PositionMode = mode.SelectedValue as string,
                    Theme = theme.SelectedValue as string, DisplayMode = display.SelectedValue as string, SidebarRegion = settings.SidebarRegion, RelativeCalibrated = calibrated.IsChecked == true, RelativeX = nextX, RelativeBottom = nextBottom };
                try { next.Save(); apply(next); Close(); }
                catch { MessageBox.Show(this, "配置无效或无法写入。CLI 须为完整 .exe 路径；偏移应在允许范围内。原配置未主动覆盖。"); }
            };
            actions.Children.Add(save); Grid.SetRow(actions, 2); root.Children.Add(actions); Content = root;
            PreviewKeyDown += (sender, key) =>
            {
                if (key.Key == Key.Escape && !mode.IsDropDownOpen && !theme.IsDropDownOpen && !display.IsDropDownOpen)
                { key.Handled = true; Close(); }
            };
        }
        private static TextBox Input(StackPanel panel, string label, string value, Palette colors)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(Label(label, colors));
            var input = new TextBox { Text = value ?? "", FontSize = 13 };
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            row.Children.Add(input); panel.Children.Add(row); return input;
        }
        private static ComboBox Choice(StackPanel panel, string label, string[] items, string selected, Palette colors)
        {
            panel.Children.Add(Label(label, colors));
            var options = new List<KeyValuePair<string, string>>();
            foreach (string value in items)
            {
                string text = value == "sidebarCard" ? "右侧使用量卡片" : value == "compactBar" ? "输入区单行栏" : value == "relative" ? "相对定位" : value == "light" ? "浅色" : value == "dark" ? "深色" :
                    label == "定位方式" ? "自动定位" : "跟随系统";
                options.Add(new KeyValuePair<string, string>(value, text));
            }
            var labelView = new FrameworkElementFactory(typeof(TextBlock));
            labelView.SetBinding(TextBlock.TextProperty, new Binding("Value"));
            var choice = new ComboBox { ItemsSource = options, ItemTemplate = new DataTemplate { VisualTree = labelView }, SelectedValuePath = "Key", SelectedValue = selected };
            System.Windows.Automation.AutomationProperties.SetName(choice, label);
            panel.Children.Add(choice); return choice;
        }
        private static TextBlock Label(string value, Palette colors)
        {
            var label = UiStyles.Text(value, colors.Foreground, 13);
            label.Margin = new Thickness(0, 0, 0, 6); return label;
        }
    }
}
