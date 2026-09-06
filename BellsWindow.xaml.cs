using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;

namespace CollegeScheduleGadget
{
    public partial class BellsWindow : Window
    {
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newStyle);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private AppSettings settings;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer desktopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private bool isTopmostState = false; 

        private readonly Dictionary<string, (TimeSpan Start, TimeSpan End)> regularBells = new() { ["1"] = (new TimeSpan(8, 0, 0), new TimeSpan(9, 20, 0)), ["2"] = (new TimeSpan(9, 30, 0), new TimeSpan(10, 50, 0)), ["3"] = (new TimeSpan(11, 10, 0), new TimeSpan(12, 30, 0)), ["4"] = (new TimeSpan(12, 40, 0), new TimeSpan(14, 0, 0)), ["5"] = (new TimeSpan(14, 10, 0), new TimeSpan(15, 30, 0)), ["6"] = (new TimeSpan(15, 40, 0), new TimeSpan(17, 0, 0)), ["7"] = (new TimeSpan(17, 10, 0), new TimeSpan(18, 30, 0)), ["8"] = (new TimeSpan(18, 40, 0), new TimeSpan(20, 0, 0)) };
        private readonly Dictionary<string, (TimeSpan Start, TimeSpan End)> shortenedBells = new() { ["1"] = (new TimeSpan(8, 0, 0), new TimeSpan(9, 2, 0)), ["2"] = (new TimeSpan(9, 10, 0), new TimeSpan(10, 10, 0)), ["3"] = (new TimeSpan(10, 30, 0), new TimeSpan(11, 30, 0)), ["4"] = (new TimeSpan(11, 40, 0), new TimeSpan(12, 40, 0)), ["5"] = (new TimeSpan(12, 50, 0), new TimeSpan(13, 50, 0)), ["6"] = (new TimeSpan(14, 0, 0), new TimeSpan(15, 0, 0)), ["7"] = (new TimeSpan(15, 10, 0), new TimeSpan(16, 10, 0)) };

        public BellsWindow(AppSettings currentSettings) { InitializeComponent(); settings = currentSettings; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0112 && (wParam.ToInt32() & 0xFFF0) == 0xF020) { handled = true; return IntPtr.Zero; }

            if (msg == 0x0084 && !settings.IsBellsPinned) // WM_NCHITTEST
            {
                int x = lParam.ToInt32() & 0xFFFF; if ((x & 0x8000) != 0) x |= unchecked((int)0xFFFF0000);
                int y = (lParam.ToInt32() >> 16) & 0xFFFF; if ((y & 0x8000) != 0) y |= unchecked((int)0xFFFF0000);

                System.Windows.Point pt;
                try { pt = this.PointFromScreen(new System.Windows.Point(x, y)); } catch { return IntPtr.Zero; }

                int b = 12; 
                if (pt.X < b && pt.Y < b) { handled = true; return new IntPtr(13); }
                if (pt.X >= ActualWidth - b && pt.Y < b) { handled = true; return new IntPtr(14); }
                if (pt.X < b && pt.Y >= ActualHeight - b) { handled = true; return new IntPtr(16); }
                if (pt.X >= ActualWidth - b && pt.Y >= ActualHeight - b) { handled = true; return new IntPtr(17); }
                if (pt.X < b) { handled = true; return new IntPtr(10); }
                if (pt.X >= ActualWidth - b) { handled = true; return new IntPtr(11); }
                if (pt.Y < b) { handled = true; return new IntPtr(12); }
                if (pt.Y >= ActualHeight - b) { handled = true; return new IntPtr(15); }
            }
            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOOLWINDOW));
            this.ShowInTaskbar = false; 

            this.Left = settings.BellsLeft ?? 500; this.Top = settings.BellsTop ?? 100;
            this.Width = settings.BellsWidth ?? 280; this.Height = settings.BellsHeight ?? 430;
            
            ApplyAppearance(); UpdatePinButton(); UpdateResizeMode(); RenderBells();
            
            timer.Tick += Timer_Tick; timer.Start();
            desktopTimer.Tick += DesktopTimer_Tick; desktopTimer.Start();
        }

        public void UpdateSettings(AppSettings newSettings) { settings = newSettings; ApplyAppearance(); UpdateResizeMode(); RenderBells(); }
        private void Timer_Tick(object? sender, EventArgs e) { ClockText.Text = DateTime.Now.ToString("HH:mm:ss"); UpdateCountdown(); }

        private void RenderBells()
        {
            LessonsPanel.Children.Clear();
            SelectedDayText.Text = settings.UseShortenedBells ? "Скорочений розклад" : "Звичайний розклад";
            var activeBells = settings.UseShortenedBells ? shortenedBells : regularBells;
            int bottomMargin = settings.TextSize == "Small" ? 5 : 10;
            foreach (var kvp in activeBells.OrderBy(k => int.Parse(k.Key)))
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, bottomMargin) };
                panel.Children.Add(new TextBlock { Text = $"{kvp.Key} пара", Foreground = GetSecondaryTextBrush(), FontSize = GetMetaFontSize() });
                panel.Children.Add(new TextBlock { Text = $"{kvp.Value.Start:hh\\:mm} - {kvp.Value.End:hh\\:mm}", Foreground = GetPrimaryTextBrush(), FontSize = GetTitleFontSize(), FontWeight = FontWeights.Bold });
                LessonsPanel.Children.Add(panel);
            }
        }

        private double GetMetaFontSize() => settings.TextSize switch { "Small" => 11, "Large" => 14, _ => 12 };
        private double GetTitleFontSize() => settings.TextSize switch { "Small" => 16, "Large" => 20, _ => 18 };

        private void UpdateCountdown()
        {
            var now = DateTime.Now.TimeOfDay; var activeBells = settings.UseShortenedBells ? shortenedBells : regularBells;
            foreach (var kvp in activeBells.OrderBy(k => int.Parse(k.Key)))
            {
                if (now < kvp.Value.Start) { CountdownText.Text = $"До {kvp.Key} пари: {FormatCountdown(kvp.Value.Start - now)}"; return; }
                if (now < kvp.Value.End) { CountdownText.Text = $"Йде {kvp.Key} пара: {FormatCountdown(kvp.Value.End - now)}"; return; }
            }
            CountdownText.Text = "Пари завершені.";
        }

        private static string FormatCountdown(TimeSpan d) => d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"mm\:ss");

        private void PinButton_Click(object sender, RoutedEventArgs e) { settings.IsBellsPinned = !settings.IsBellsPinned; UpdatePinButton(); UpdateResizeMode(); SaveSettings(); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        private void UpdatePinButton() => PinButton.Opacity = settings.IsBellsPinned ? 1 : 0.65;
        private void UpdateResizeMode() { ResizeMode = settings.IsBellsPinned ? ResizeMode.NoResize : ResizeMode.CanResize; }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (settings.IsBellsPinned) return; try { DragMove(); } catch { } finally { SaveSettings(); } }
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) { if (!settings.IsBellsPinned && IsLoaded) SaveSettings(); }

        private void SaveSettings() { if (IsLoaded) { settings.BellsLeft = Left; settings.BellsTop = Top; settings.BellsWidth = Width; settings.BellsHeight = Height; } SettingsStore.Save(settings); }

        private void ApplyAppearance()
        {
            var backgroundHex = settings.BellsTheme switch { "Violet" => "#C026163D", "Ember" => "#C03D2118", "Graphite" => "#C02A2E35", "Cyberpunk" => "#E6101018", "Matcha" => "#C0202A22", "Ocean" => "#C00B1B2E", _ => "#C0141414" };
            ScheduleCard.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backgroundHex));
            ScheduleCard.Opacity = settings.Opacity;
            int padding = settings.TextSize == "Small" ? 6 : 10; ScheduleCard.Padding = new Thickness(padding);
            GroupTitle.Foreground = GetPrimaryTextBrush(); StatusText.Foreground = GetSecondaryTextBrush(); SelectedDayText.Foreground = GetSecondaryTextBrush(); CountdownText.Foreground = GetAccentBrush();
        }

        private System.Windows.Media.Brush GetPrimaryTextBrush() => settings.BellsTheme switch { "Graphite" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 246, 248)), "Matcha" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(235, 245, 235)), "Ocean" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 240, 255)), "Cyberpunk" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 255)), _ => System.Windows.Media.Brushes.White };
        private System.Windows.Media.Brush GetSecondaryTextBrush() => settings.BellsTheme switch { "Violet" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 202, 255)), "Ember" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 218, 192)), "Graphite" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(195, 202, 210)), "Cyberpunk" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 40, 130)), "Matcha" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(165, 205, 175)), "Ocean" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 195, 235)), _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(153, 255, 255, 255)) };
        private System.Windows.Media.Brush GetAccentBrush() => settings.BellsTheme switch { "Violet" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 165, 255)), "Ember" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 178, 105)), "Graphite" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(144, 198, 255)), "Cyberpunk" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 240, 255)), "Matcha" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 235, 150)), "Ocean" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 220, 255)), _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 217, 255)) };

        private void DesktopTimer_Tick(object? sender, EventArgs e) 
        { 
            var hwnd = new WindowInteropHelper(this).Handle; 
            var fg = GetForegroundWindow(); 
            GetWindowThreadProcessId(fg, out uint fgProcId);
            bool isOurApp = fgProcId == (uint)Environment.ProcessId;
            bool desktopIsActive = fg == hwnd || IsDesktopWindow(fg) || isOurApp; 
            
            if (desktopIsActive && !isTopmostState) 
            { 
                this.Topmost = true; 
                isTopmostState = true; 
            }
            else if (!desktopIsActive && isTopmostState) 
            { 
                this.Topmost = false; 
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW); 
                isTopmostState = false; 
            }
        }
        
        private static bool IsDesktopWindow(IntPtr windowHandle) { if (windowHandle == IntPtr.Zero) return true; var className = new StringBuilder(256); GetClassName(windowHandle, className, className.Capacity); return className.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView"; }
        private void Window_StateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; var hwnd = new WindowInteropHelper(this).Handle; SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW); } }
    }
}