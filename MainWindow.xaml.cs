using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CollegeScheduleGadget
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newStyle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr windowHandle,
            StringBuilder className, int maxCount);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private readonly DispatcherTimer desktopTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        public MainWindow()
        {
            Application.LoadComponent(this, new Uri(
                "/CollegeScheduleGadget;component/MainWindow.xaml",
                UriKind.Relative));
            
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE,
                new IntPtr(exStyle | WS_EX_TOOLWINDOW));

            this.Left = 100;
            this.Top = 100;
            StateChanged += MainWindow_StateChanged;
            desktopTimer.Tick += DesktopTimer_Tick;
            desktopTimer.Start();
        }

        private void DesktopTimer_Tick(object? sender, EventArgs e)
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            var foregroundWindow = GetForegroundWindow();
            var desktopIsActive = foregroundWindow == windowHandle || IsDesktopWindow(foregroundWindow);

            SetWindowPos(windowHandle,
                desktopIsActive ? HWND_TOPMOST : HWND_NOTOPMOST,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

            if (Topmost != desktopIsActive)
            {
                Topmost = desktopIsActive;
            }
        }

        private static bool IsDesktopWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return true;
            }

            var className = new StringBuilder(256);
            GetClassName(windowHandle, className, className.Capacity);
            return className.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView";
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }));
        }

        // Дозволяємо перетягувати віджет мишкою за будь-яке місце
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }
    }
}