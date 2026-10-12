using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexUsageMonitor
{
    internal sealed class UsageCard : Border
    {
        private readonly Palette colors;
        private readonly MeterRow five, week;
        private readonly TextBlock count, stateText;
        private readonly Border countBadge;
        private readonly Button refresh;
        private readonly StackPanel detailRows;
        private readonly ScrollViewer detailsView;
        private readonly TextBlock disclosure;
        internal UsageCard(Palette palette, Action refreshAction, Action hideAction)
        {
            colors = palette; Resources = UiStyles.Resources(colors);
            Background = colors.Background; BorderBrush = colors.Border; BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(16); Padding = new Thickness(16);
            SnapsToDevicePixels = true; UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            var content = new StackPanel();
            var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = UiStyles.Text("使用量", colors.Foreground, 20); title.FontWeight = FontWeights.SemiBold;
            header.Children.Add(title);
            var close = ActionButton("\uE711", "隐藏卡片，托盘可重新显示", hideAction); Grid.SetColumn(close, 1); header.Children.Add(close);
            content.Children.Add(header);
            five = new MeterRow(colors, "5 小时使用限额"); week = new MeterRow(colors, "每周使用限额");
            var limits = new StackPanel(); limits.Children.Add(five);
            limits.Children.Add(new Border { Height = 1, Background = colors.Border }); limits.Children.Add(week);
            content.Children.Add(Frame(limits, new Thickness(0), 14));
            var reset = new Grid { Margin = new Thickness(16, 14, 16, 14) };
            reset.ColumnDefinitions.Add(new ColumnDefinition()); reset.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var resetLabel = UiStyles.Text("使用限额重置", colors.Foreground, 15); resetLabel.FontWeight = FontWeights.SemiBold;
            resetLabel.Margin = new Thickness(0, 0, 8, 0); reset.Children.Add(resetLabel);
            count = UiStyles.Text("暂无数据", colors.Muted, 13); count.FontWeight = FontWeights.SemiBold; count.MaxWidth = 140;
            countBadge = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 10, 4), Child = count, MaxWidth = 160 };
            Grid.SetColumn(countBadge, 1); reset.Children.Add(countBadge);
            disclosure = UiStyles.Text("⌄", colors.Muted, 15); disclosure.Margin = new Thickness(8, 0, 0, 0);
            reset.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetColumn(disclosure, 2); reset.Children.Add(disclosure);
            var toggle = new Button { Height = Double.NaN, MinHeight = 54, Content = reset, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Focusable = false, IsTabStop = false, ToolTip = "查看每张重置卡的有效期（只读）" };
            System.Windows.Automation.AutomationProperties.SetName(toggle, "展开或收起重置卡有效期");
            detailRows = new StackPanel { Margin = new Thickness(16, 0, 16, 12) };
            detailsView = new ScrollViewer { Content = detailRows, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed, Focusable = false, IsTabStop = false };
            toggle.Click += delegate { bool expanded = detailsView.Visibility != Visibility.Visible; detailsView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed; disclosure.Text = expanded ? "⌃" : "⌄"; DependencyObject ancestor = detailsView; while (ancestor != null) { var element = ancestor as UIElement; if (element != null) element.InvalidateMeasure(); ancestor = VisualTreeHelper.GetParent(ancestor); } };
            var resetContent = new StackPanel(); resetContent.Children.Add(toggle); resetContent.Children.Add(detailsView);
            content.Children.Add(Frame(resetContent, new Thickness(0, 14, 0, 0), 14));
            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition());
            refresh = ActionButton("\uE72C", "刷新额度与重置卡数量（每 60 秒自动刷新）", refreshAction);
            footer.Children.Add(refresh);
            stateText = UiStyles.Text("正在连接", colors.Muted, 13); stateText.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(stateText, 1); footer.Children.Add(stateText);
            content.Children.Add(footer); Child = content;
        }
        private Border Frame(UIElement child, Thickness margin, double radius)
        {
            return new Border { BorderBrush = colors.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Child = child, Margin = margin };
        }
        private Button ActionButton(string glyph, string description, Action action)
        {
            var button = new Button { Width = 28, Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 },
                Style = (Style)Resources["RefreshButton"], Focusable = false, IsTabStop = false, ToolTip = description };
            System.Windows.Automation.AutomationProperties.SetName(button, description);
            button.Click += delegate { if (action != null) action(); }; return button;
        }
        internal double MeasureHeight(double width)
        {
            Width = width; five.Narrow = width < 420; week.Narrow = width < 420;
            Measure(new Size(width, Double.PositiveInfinity)); return Math.Ceiling(DesiredSize.Height);
        }
        internal void Update(QuotaState state)
        {
            var snapshot = state.Snapshot;
            five.Update(snapshot == null ? null : snapshot.FiveHour); week.Update(snapshot == null ? null : snapshot.Weekly);
            long? cards = snapshot == null ? null : snapshot.ResetCards;
            bool longCount = cards.HasValue && cards.Value > 999999;
            count.Text = cards.HasValue ? (longCount ? "可用\n" : "可用 ") + cards.Value + " 张" : "暂无数据";
            countBadge.Width = longCount ? 150 : Double.NaN;
            count.MaxWidth = longCount ? 130 : 140;
            count.Foreground = cards.HasValue ? PaletteBrush(colors.Dark ? "#83DBAA" : "#087A3A") : colors.Muted;
            countBadge.Background = cards.HasValue ? PaletteBrush(colors.Dark ? "#234334" : "#D9F3E5") : colors.Track;
            detailRows.Children.Clear();
            var details = snapshot == null ? null : snapshot.ResetDetails;
            if (details == null) detailRows.Children.Add(UiStyles.Text("接口暂未提供逐张有效期", colors.Muted, 13));
            else if (details.Count == 0) detailRows.Children.Add(UiStyles.Text(cards == 0 ? "暂无可用重置卡" : "暂无可用卡明细", colors.Muted, 13));
            else
            {
                for (int i = 0; i < details.Count; i++)
                {
                    if (i > 0) detailRows.Children.Add(new Border { Height = 1, Background = colors.Border, Margin = new Thickness(0, 10, 0, 10) });
                    var title = UiStyles.Text((i + 1) + ". " + details[i].Title, colors.Foreground, 13); title.FontWeight = FontWeights.SemiBold; title.TextWrapping = TextWrapping.Wrap;
                    detailRows.Children.Add(title);
                    var validity = UiStyles.Text(details[i].ValidityText, colors.Muted, 13); validity.Margin = new Thickness(0, 4, 0, 0); validity.TextWrapping = TextWrapping.Wrap;
                    detailRows.Children.Add(validity);
                }
                if (cards.HasValue && cards.Value > details.Count) detailRows.Children.Add(UiStyles.Text("接口仅提供 " + details.Count + " 张明细，其余暂无数据", colors.Muted, 13));
            }
            stateText.Text = state.Status(DateTimeOffset.UtcNow);
            stateText.Foreground = state.IsStale(DateTimeOffset.UtcNow) || state.Error != null ? colors.Red : colors.Muted;
            refresh.IsEnabled = !state.Refreshing; ToolTip = state.Summary(DateTimeOffset.UtcNow);
        }
        private static Brush PaletteBrush(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        private sealed class MeterRow : Grid
        {
            private readonly Palette colors;
            private readonly TextBlock label, reset, remaining;
            private readonly Grid meter;
            private readonly Border track, fill;
            private double percentage;
            internal bool Narrow { set { ArrangeMode(value); } }
            internal MeterRow(Palette palette, string text)
            {
                colors = palette; Margin = new Thickness(16, 16, 16, 16);
                label = UiStyles.Text(text, colors.Foreground, 15); label.FontWeight = FontWeights.SemiBold;
                reset = UiStyles.Text("重置时间暂无数据", colors.Muted, 13); reset.Margin = new Thickness(0, 3, 0, 0);
                remaining = UiStyles.Text("剩余 暂无数据", colors.Muted, 13); remaining.HorizontalAlignment = HorizontalAlignment.Right;
                fill = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
                track = new Border { Height = 8, Background = colors.Track, CornerRadius = new CornerRadius(4), Child = fill, VerticalAlignment = VerticalAlignment.Center };
                meter = new Grid(); meter.Children.Add(track);
                track.SizeChanged += delegate { fill.Width = Math.Max(0, track.ActualWidth * percentage / 100); };
                Children.Add(label); Children.Add(reset); Children.Add(remaining); Children.Add(meter); ArrangeMode(true);
            }
            private void ArrangeMode(bool narrow)
            {
                ColumnDefinitions.Clear(); RowDefinitions.Clear();
                if (narrow)
                {
                    ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
                    RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(label, 0); Grid.SetColumn(label, 0); Grid.SetRow(remaining, 0); Grid.SetColumn(remaining, 1);
                    Grid.SetRow(reset, 1); Grid.SetColumn(reset, 0); Grid.SetRow(meter, 1); Grid.SetColumn(meter, 1);
                    remaining.Margin = new Thickness(8, 0, 0, 0); meter.Margin = new Thickness(12, 8, 0, 0);
                    reset.TextWrapping = TextWrapping.Wrap;
                }
                else
                {
                    ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(146) }); ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                    RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition());
                    Grid.SetRow(label, 0); Grid.SetColumn(label, 0); Grid.SetRow(reset, 1); Grid.SetColumn(reset, 0);
                    Grid.SetRow(meter, 0); Grid.SetRowSpan(meter, 2); Grid.SetColumn(meter, 1);
                    Grid.SetRow(remaining, 0); Grid.SetRowSpan(remaining, 2); Grid.SetColumn(remaining, 2);
                    remaining.Margin = new Thickness(8, 0, 0, 0); meter.Margin = new Thickness(0, 0, 0, 0);
                }
                if (narrow) { Grid.SetRowSpan(meter, 1); Grid.SetRowSpan(remaining, 1); }
            }
            internal void Update(QuotaWindow window)
            {
                double? value = window == null ? null : window.Remaining; percentage = value ?? 0;
                remaining.Text = value.HasValue ? "剩余 " + QuotaState.Percent(window) : "剩余 暂无数据";
                bool low = value.HasValue && value.Value < 20;
                remaining.Foreground = low ? colors.Red : colors.Muted;
                fill.Background = !value.HasValue ? colors.Muted : low ? colors.Red : colors.Foreground;
                fill.Width = Math.Max(0, track.ActualWidth * percentage / 100);
                var time = window == null ? null : window.LocalReset;
                reset.Text = time.HasValue ? "将于 " + time.Value.ToString("MM-dd HH:mm") + " 重置" : "重置时间暂无数据";
            }
        }
    }
}
