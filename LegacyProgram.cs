using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RagdollPet
{
    public enum PetState { Idle, Walking, Sleeping }

    public sealed class PetWindow : Window
    {
        readonly Random random = new Random();
        readonly DispatcherTimer frameTimer = new DispatcherTimer();
        readonly DispatcherTimer decisionTimer = new DispatcherTimer();
        readonly Image pet = new Image();
        readonly Border bubble = new Border();
        readonly TextBlock speech = new TextBlock();
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly RotateTransform rotate = new RotateTransform(0);
        readonly TranslateTransform offset = new TranslateTransform(0, 0);
        readonly Forms.NotifyIcon tray;
        PetState state = PetState.Idle;
        DateTime nextDecision;
        DateTime bubbleUntil;
        double targetX, phase;
        bool dragging;
        Point dragStart, windowStart;

        public PetWindow()
        {
            Title = "团团桌宠"; Width = 310; Height = 255;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent; Topmost = true;
            ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;

            var root = new Grid { Background = Brushes.Transparent };
            Content = root;
            bubble.HorizontalAlignment = HorizontalAlignment.Center;
            bubble.VerticalAlignment = VerticalAlignment.Top;
            bubble.Margin = new Thickness(8, 0, 8, 0);
            bubble.Padding = new Thickness(13, 7, 13, 7);
            bubble.CornerRadius = new CornerRadius(15);
            bubble.Background = new SolidColorBrush(Color.FromArgb(239, 255, 255, 255));
            bubble.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 143, 129, 120));
            bubble.BorderThickness = new Thickness(1); bubble.Opacity = 0;
            speech.Foreground = new SolidColorBrush(Color.FromRgb(73, 62, 57)); speech.FontSize = 14;
            bubble.Child = speech; root.Children.Add(bubble);

            var imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "pet.png");
            pet.Source = new BitmapImage(new Uri(imagePath)); pet.Stretch = Stretch.Uniform;
            pet.Margin = new Thickness(5, 30, 5, 0); pet.RenderTransformOrigin = new Point(.5, 1);
            var transforms = new TransformGroup();
            transforms.Children.Add(scale); transforms.Children.Add(rotate); transforms.Children.Add(offset);
            pet.RenderTransform = transforms; root.Children.Add(pet);
            pet.MouseLeftButtonDown += MouseDown; pet.MouseMove += MouseMove;
            pet.MouseLeftButtonUp += MouseUp; pet.MouseRightButtonUp += RightClick;

            frameTimer.Interval = TimeSpan.FromMilliseconds(30); frameTimer.Tick += Frame;
            decisionTimer.Interval = TimeSpan.FromSeconds(1); decisionTimer.Tick += Decide;
            Loaded += LoadedWindow; Closing += ClosingWindow;

            tray = new Forms.NotifyIcon { Text = "团团桌宠", Icon = SystemIcons.Application, Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("叫团团过来", null, delegate { Dispatcher.Invoke(Play); });
            menu.Items.Add("睡一会儿", null, delegate { Dispatcher.Invoke(Sleep); });
            menu.Items.Add("继续散步", null, delegate { Dispatcher.Invoke(StartWalking); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Dispatcher.Invoke(Close); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Dispatcher.Invoke(Play); };
        }

        void LoadedWindow(object sender, RoutedEventArgs e)
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - Width - 35; Top = area.Bottom - Height; targetX = Left;
            Next(3, 7); frameTimer.Start(); decisionTimer.Start(); Say("我来啦，摸摸我吧～", 3);
        }

        void Frame(object sender, EventArgs e)
        {
            if (dragging) return; phase += .16; Rect area = SystemParameters.WorkArea; Top = area.Bottom - Height;
            if (state == PetState.Walking)
            {
                double d = targetX - Left;
                if (Math.Abs(d) < 3) { state = PetState.Idle; Next(4, 10); }
                else { Left = Math.Max(area.Left, Math.Min(area.Right - Width, Left + Math.Sign(d) * 2.25)); scale.ScaleX = d >= 0 ? 1 : -1; offset.Y = -Math.Abs(Math.Sin(phase)) * 4; rotate.Angle = Math.Sin(phase) * 1.2; }
            }
            else if (state == PetState.Idle) { offset.Y = Math.Sin(phase * .25) * 1.5; rotate.Angle *= .86; }
            if (DateTime.Now > bubbleUntil && bubble.Opacity > 0) bubble.Opacity = Math.Max(0, bubble.Opacity - .08);
        }

        void Decide(object sender, EventArgs e)
        {
            if (dragging || DateTime.Now < nextDecision) return;
            if (state == PetState.Sleeping) { Wake(); return; }
            int roll = random.Next(100);
            if (roll < 55) StartWalking(); else if (roll < 75) Sleep();
            else { state = PetState.Idle; Say(random.Next(2) == 0 ? "今天也要开心呀" : "我在看着你～", 2.5); Next(5, 11); }
        }

        void StartWalking() { Rect a = SystemParameters.WorkArea; targetX = a.Left + random.NextDouble() * Math.Max(1, a.Width - Width); state = PetState.Walking; pet.Opacity = 1; scale.ScaleY = 1; Next(10, 18); }
        void Sleep() { state = PetState.Sleeping; pet.Opacity = .78; scale.ScaleY = .78; offset.Y = 18; rotate.Angle = scale.ScaleX > 0 ? 4 : -4; Say("呼……zzz", 3); Next(12, 22); }
        void Wake() { state = PetState.Idle; pet.Opacity = 1; scale.ScaleY = 1; offset.Y = 0; rotate.Angle = 0; Say("睡醒啦！", 2); Next(3, 7); }
        void Play() { state = PetState.Idle; pet.Opacity = 1; scale.ScaleY = 1; Say(random.Next(3) == 0 ? "喵～" : "再摸一下！", 2.2); offset.Y = -15; var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) }; t.Tick += delegate { t.Stop(); offset.Y = 0; }; t.Start(); Next(3, 7); }
        void Say(string text, double seconds) { speech.Text = text; bubble.Opacity = 1; bubbleUntil = DateTime.Now.AddSeconds(seconds); }
        void Next(int min, int max) { nextDecision = DateTime.Now.AddSeconds(random.Next(min, max + 1)); }

        void MouseDown(object s, MouseButtonEventArgs e) { dragging = true; dragStart = PointToScreen(e.GetPosition(this)); windowStart = new Point(Left, Top); pet.CaptureMouse(); state = PetState.Idle; }
        void MouseMove(object s, MouseEventArgs e) { if (!dragging || e.LeftButton != MouseButtonState.Pressed) return; Point p = PointToScreen(e.GetPosition(this)); Left = windowStart.X + p.X - dragStart.X; Top = windowStart.Y + p.Y - dragStart.Y; rotate.Angle = Math.Max(-10, Math.Min(10, (p.X - dragStart.X) / 10)); }
        void MouseUp(object s, MouseButtonEventArgs e) { if (!dragging) return; double moved = Math.Abs(Left - windowStart.X) + Math.Abs(Top - windowStart.Y); dragging = false; pet.ReleaseMouseCapture(); rotate.Angle = 0; Top = SystemParameters.WorkArea.Bottom - Height; if (moved < 10) Play(); else { Say("放在这里也可以～", 2); Next(4, 8); } }
        void RightClick(object s, MouseButtonEventArgs e) { var m = new ContextMenu(); Add(m, "摸摸", Play); Add(m, "睡觉", Sleep); Add(m, "散步", StartWalking); m.Items.Add(new Separator()); Add(m, "退出桌宠", Close); m.IsOpen = true; }
        static void Add(ContextMenu menu, string text, Action action) { var item = new MenuItem { Header = text }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        void ClosingWindow(object s, CancelEventArgs e) { tray.Visible = false; tray.Dispose(); }
    }

    public static class Program
    {
        [STAThread]
        public static void Main() { var app = new Application(); app.Run(new PetWindow()); }
    }
}
