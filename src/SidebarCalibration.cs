using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodexUsageMonitor
{
    internal sealed class SidebarCalibration : Window
    {
        private readonly Rect client;
        private readonly double scale;
        private readonly IntPtr target;
        private readonly Action<SidebarRegion> save;
        private readonly Canvas canvas;
        private readonly Rectangle outline;
        private readonly TextBlock instructions;
        private Point start;
        private bool selecting;
        internal SidebarCalibration(IntPtr target, Action<SidebarRegion> saved)
        {
            this.target = target; client = Native.Client(target); scale = Native.Scale(target); save = saved;
            if (client.IsEmpty || Native.IsIconic(target)) throw new ArgumentException();
            Title = "框选右侧卡片区域"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(100, 30, 35, 40)); ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
            Left = client.Left / scale; Top = client.Top / scale; Width = client.Width / scale; Height = client.Height / scale;
            canvas = new Canvas { Background = Brushes.Transparent }; Content = canvas;
            instructions = new TextBlock { Text = "拖动框选右侧空白区域（至少 324 DIP 宽） · 松开保存 · Esc 取消", FontFamily = UiStyles.Font,
                FontSize = 15, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(220, 30, 35, 40)), Padding = new Thickness(12), IsHitTestVisible = false };
            Canvas.SetLeft(instructions, 20); Canvas.SetTop(instructions, 20); canvas.Children.Add(instructions);
            outline = new Rectangle { Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), IsHitTestVisible = false };
            canvas.Children.Add(outline);
            Loaded += delegate
            {
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), (int)client.Left, (int)client.Top, (int)client.Width, (int)client.Height, Native.SWP_SHOWWINDOW);
            };
            MouseLeftButtonDown += delegate { start = Mouse.GetPosition(canvas); selecting = true; canvas.CaptureMouse(); };
            MouseMove += delegate
            {
                if (!selecting) return;
                var box = new Rect(start, Mouse.GetPosition(canvas));
                Canvas.SetLeft(outline, box.Left); Canvas.SetTop(outline, box.Top); outline.Width = box.Width; outline.Height = box.Height;
            };
            MouseLeftButtonUp += delegate
            {
                if (!selecting) return; selecting = false; canvas.ReleaseMouseCapture();
                var selected = new Rect(canvas.PointToScreen(start), canvas.PointToScreen(Mouse.GetPosition(canvas)));
                try
                {
                    if (!Native.IsCodex(target) || Native.IsIconic(target) || Native.Client(target) != client || Math.Abs(Native.Scale(target) - scale) > .01)
                        throw new ArgumentException();
                    var region = SidebarRegion.FromSelection(selected, client, scale); save(region); Close();
                }
                catch { instructions.Text = "区域无效或无法保存，请框选至少 324 DIP 宽、250 DIP 高的右侧区域 · Esc 取消"; }
            };
            PreviewKeyDown += (sender, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }
    }
}
