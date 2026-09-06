using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace CollegeScheduleGadget
{
    public partial class SettingsWindow : Window
    {
        private readonly IReadOnlyList<string> availableGroups;
        public AppSettings Settings { get; }
        public event EventHandler? SettingsApplied;
        public event EventHandler? TestRefreshRequested;

        public SettingsWindow(AppSettings settings, IReadOnlyList<string> groups)
        {
            InitializeComponent();
            Settings = new AppSettings
            {
                Group = settings.Group, Opacity = settings.Opacity, Left = settings.Left, Top = settings.Top, Width = settings.Width, Height = settings.Height,
                IsPinned = settings.IsPinned, StartWithWindows = settings.StartWithWindows, DisableNotifications = settings.DisableNotifications,
                Theme = settings.Theme, WidgetStyle = settings.WidgetStyle, TextSize = settings.TextSize, CustomColor = settings.CustomColor,
                ShowFullWeek = settings.ShowFullWeek, IsBellsMode = settings.IsBellsMode, UseShortenedBells = settings.UseShortenedBells, BellsTheme = settings.BellsTheme
            };
            availableGroups = groups; GroupInput.Text = Settings.Group; OpacitySlider.Value = Math.Clamp(Settings.Opacity, 0.25, 1.0);
            ThemeComboBox.SelectedValue = Settings.Theme; StyleComboBox.SelectedValue = Settings.WidgetStyle; TextSizeSelector.SelectedValue = Settings.TextSize;
            ColorPreview.Tag = Settings.CustomColor; ColorPreview.Background = GetCustomColorBrush();
            StartWithWindowsCheckBox.IsChecked = Settings.StartWithWindows; DisableNotificationsCheckBox.IsChecked = Settings.DisableNotifications; ShowFullWeekCheckBox.IsChecked = Settings.ShowFullWeek;
            IsBellsModeCheckBox.IsChecked = Settings.IsBellsMode; UseShortenedBellsCheckBox.IsChecked = Settings.UseShortenedBells; BellsThemeComboBox.SelectedValue = Settings.BellsTheme;
            ShowPanel(GeneralPanel, GeneralTabButton, "Домашня сторінка");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0084) // WM_NCHITTEST
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
            try { var pos = System.IO.File.ReadAllText("settings_pos.txt").Split(';'); this.Left = double.Parse(pos[0]); this.Top = double.Parse(pos[1]); } 
            catch { this.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        }
        
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { try { System.IO.File.WriteAllText("settings_pos.txt", $"{this.Left};{this.Top}"); } catch { } }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left && e.GetPosition(this).Y < 45) this.DragMove(); }
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void MaximizeBtn_Click(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void GroupInput_TextChanged(object sender, TextChangedEventArgs e) { var search = GroupInput.Text.Trim(); if (search.Length == 0) { GroupSuggestions.Visibility = Visibility.Collapsed; return; } var matches = availableGroups.Where(group => StartsWithGroupSearch(group, search)).Take(10).ToList(); GroupSuggestions.ItemsSource = matches; GroupSuggestions.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
        private void GroupSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (GroupSuggestions.SelectedItem is not string group) return; GroupInput.Text = group; GroupInput.CaretIndex = GroupInput.Text.Length; GroupSuggestions.SelectedItem = null; GroupSuggestions.Visibility = Visibility.Collapsed; }
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (OpacityValue is not null) OpacityValue.Text = $"{Math.Round(e.NewValue * 100)}%"; }
        private void OpacitySlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { var slider = (Slider)sender; var position = e.GetPosition(slider); var ratio = Math.Clamp(position.X / slider.ActualWidth, 0, 1); slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum); e.Handled = true; }
        private void GeneralTab_Click(object sender, RoutedEventArgs e) => ShowPanel(GeneralPanel, GeneralTabButton, "Домашня сторінка");
        private void ScheduleTab_Click(object sender, RoutedEventArgs e) => ShowPanel(SchedulePanel, ScheduleTabButton, "Розклад та вигляд");
        private void BellsTab_Click(object sender, RoutedEventArgs e) => ShowPanel(BellsPanel, BellsTabButton, "Режим Дзвінків");
        private void AboutTab_Click(object sender, RoutedEventArgs e) => ShowPanel(AboutPanel, AboutTabButton, "Про програму");
        
        private void ShowPanel(UIElement selectedPanel, System.Windows.Controls.Button activeBtn, string title)
        {
            GeneralPanel.Visibility = selectedPanel == GeneralPanel ? Visibility.Visible : Visibility.Collapsed; SchedulePanel.Visibility = selectedPanel == SchedulePanel ? Visibility.Visible : Visibility.Collapsed; BellsPanel.Visibility = selectedPanel == BellsPanel ? Visibility.Visible : Visibility.Collapsed; AboutPanel.Visibility = selectedPanel == AboutPanel ? Visibility.Visible : Visibility.Collapsed;
            ContentTitle.Text = title; GeneralTabButton.Tag = activeBtn == GeneralTabButton ? "Active" : ""; ScheduleTabButton.Tag = activeBtn == ScheduleTabButton ? "Active" : ""; BellsTabButton.Tag = activeBtn == BellsTabButton ? "Active" : ""; AboutTabButton.Tag = activeBtn == AboutTabButton ? "Active" : "";
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GroupInput.Text)) { System.Windows.MessageBox.Show("Введіть групу.", "Налаштування", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            Settings.Group = GroupInput.Text.Trim(); Settings.Opacity = OpacitySlider.Value; Settings.Theme = (ThemeComboBox.SelectedValue as string) ?? "Midnight"; Settings.WidgetStyle = (StyleComboBox.SelectedValue as string) ?? "Minimalism"; Settings.TextSize = (TextSizeSelector.SelectedValue as string) ?? "Medium"; Settings.CustomColor = ColorPreview.Tag as string ?? ""; Settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true; Settings.DisableNotifications = DisableNotificationsCheckBox.IsChecked == true; Settings.ShowFullWeek = ShowFullWeekCheckBox.IsChecked == true; Settings.IsBellsMode = IsBellsModeCheckBox.IsChecked == true; Settings.UseShortenedBells = UseShortenedBellsCheckBox.IsChecked == true; Settings.BellsTheme = (BellsThemeComboBox.SelectedValue as string) ?? "Cyberpunk";
            SettingsApplied?.Invoke(this, EventArgs.Empty); ShowToast();
        }
        
        private void ShowToast()
        {
            var storyboard = new Storyboard();
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)); Storyboard.SetTarget(fadeIn, ToastNotification); Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            var moveUp = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(moveUp, ToastNotification); Storyboard.SetTargetProperty(moveUp, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromSeconds(2) }; Storyboard.SetTarget(fadeOut, ToastNotification); Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            var moveUpFurther = new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromSeconds(2), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } }; Storyboard.SetTarget(moveUpFurther, ToastNotification); Storyboard.SetTargetProperty(moveUpFurther, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            storyboard.Children.Add(fadeIn); storyboard.Children.Add(moveUp); storyboard.Children.Add(fadeOut); storyboard.Children.Add(moveUpFurther); storyboard.Begin();
        }
        
        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) { UpdateStatusText.Text = "Перевіряю оновлення..."; try { var update = await UpdateService.CheckAsync(); UpdateStatusText.Text = update is null ? $"Оновлень немає. Поточна версія {UpdateService.CurrentVersion}." : $"Доступна версія {update.Version}."; } catch { UpdateStatusText.Text = "Помилка."; } }
        private void TestRefreshButton_Click(object sender, RoutedEventArgs e) { TestRefreshRequested?.Invoke(this, EventArgs.Empty); UpdateStatusText.Text = "Запущено."; }
        private void CustomColorButton_Click(object sender, RoutedEventArgs e) { using var dialog = new Forms.ColorDialog { FullOpen = true, AllowFullOpen = true }; if (dialog.ShowDialog() != Forms.DialogResult.OK) return; Settings.CustomColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"; ColorPreview.Tag = Settings.CustomColor; ColorPreview.Background = GetCustomColorBrush(); }
        private void ResetColorButton_Click(object sender, RoutedEventArgs e) { Settings.CustomColor = ""; ColorPreview.Tag = ""; ColorPreview.Background = System.Windows.Media.Brushes.Transparent; }
        private System.Windows.Media.Brush GetCustomColorBrush() { if (ColorPreview.Tag is not string value || string.IsNullOrWhiteSpace(value)) return System.Windows.Media.Brushes.Transparent; try { return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)); } catch { return System.Windows.Media.Brushes.Transparent; } }
        private System.Windows.Controls.ComboBox TextSizeSelector => (System.Windows.Controls.ComboBox)FindName("TextSizeComboBox")!;
        private Border ColorPreview => (Border)FindName("CustomColorPreview")!;
        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
        private static bool StartsWithGroupSearch(string group, string search) { if (group.StartsWith(search, StringComparison.OrdinalIgnoreCase)) return true; var ukrainianKeyboardSearch = search.Replace('A', 'А').Replace('a', 'а').Replace('K', 'К').Replace('k', 'к'); return group.StartsWith(ukrainianKeyboardSearch, StringComparison.OrdinalIgnoreCase); }
        private void TgSupportButton_Click(object sender, RoutedEventArgs e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://t.me/ТВІЙ_НІКНЕЙМ") { UseShellExecute = true });
        private void DonateButton_Click(object sender, RoutedEventArgs e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://send.monobank.ua/jar/ТВОЯ_БАНКА") { UseShellExecute = true });
    }
}