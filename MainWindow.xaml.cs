using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CollegeScheduleGadget
{
    public partial class MainWindow : Window
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
        
        private readonly DispatcherTimer desktopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        private readonly ScheduleService scheduleService = new ScheduleService();
        private Forms.NotifyIcon? notificationIcon;
        private System.Drawing.Icon? notificationIconImage;
        private readonly HashSet<string> notifiedLessons = new();
        private IReadOnlyList<string> availableGroups = Array.Empty<string>();
        private IReadOnlyDictionary<string, string> teacherFullNames = new Dictionary<string, string>();
        private IReadOnlyList<WeekCycle> weekCycles = Array.Empty<WeekCycle>();
        private AppSettings settings = new AppSettings();
        private SettingsWindow? settingsWindow;
        private BellsWindow? bellsWindow; 
        private List<ScheduleLesson> todayLessons = new();
        private Dictionary<string, List<ScheduleLesson>> currentSchedule = new();
        private Dictionary<int, Dictionary<string, List<ScheduleLesson>>> cachedSchedules = new();
        
        private DateTime selectedDate = DateTime.Today;
        private int currentWeekNumber;
        private int selectedDayIndex;
        private bool isTopmostState = false; 
        
        private static readonly string[] ScheduleDays = { "понеділок", "вівторок", "середа", "четвер", "п'ятниця", "субота", "неділя" };
        private readonly Dictionary<string, string> lessonTimes = new() { ["1"] = "08:00 - 09:20", ["2"] = "09:30 - 10:50", ["3"] = "11:10 - 12:30", ["4"] = "12:40 - 14:00", ["5"] = "14:10 - 15:30", ["6"] = "15:40 - 17:00" };
        private readonly Dictionary<string, (TimeSpan Start, TimeSpan End)> lessonPeriods = new() { ["1"] = (new TimeSpan(8, 0, 0), new TimeSpan(9, 20, 0)), ["2"] = (new TimeSpan(9, 30, 0), new TimeSpan(10, 50, 0)), ["3"] = (new TimeSpan(11, 10, 0), new TimeSpan(12, 30, 0)), ["4"] = (new TimeSpan(12, 40, 0), new TimeSpan(14, 0, 0)), ["5"] = (new TimeSpan(14, 10, 0), new TimeSpan(15, 30, 0)), ["6"] = (new TimeSpan(15, 40, 0), new TimeSpan(17, 0, 0)) };

        public MainWindow() { System.Windows.Application.LoadComponent(this, new Uri("/CollegeScheduleGadget;component/MainWindow.xaml", UriKind.Relative)); }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0112 && (wParam.ToInt32() & 0xFFF0) == 0xF020) { handled = true; return IntPtr.Zero; }

            if (msg == 0x0084 && !settings.IsPinned) // WM_NCHITTEST
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

            settings = SettingsStore.Load();
            settings.Opacity = Math.Clamp(settings.Opacity, 0.25, 1.0);
            this.Left = settings.Left ?? 100; this.Top = settings.Top ?? 100;
            this.Width = settings.Width ?? 340; this.Height = settings.Height ?? 430;
            
            selectedDate = DateTime.Today; selectedDayIndex = GetDayIndex(selectedDate);
            ApplyAppearance(); ApplyWidgetSize(); UpdatePinButton(); UpdateResizeMode(); ApplyStartupSetting();
            
            Closing += MainWindow_Closing;
            desktopTimer.Tick += DesktopTimer_Tick; desktopTimer.Start();
            countdownTimer.Tick += CountdownTimer_Tick; countdownTimer.Start();
            
            notificationIconImage = LoadNotificationIcon();
            notificationIcon = new Forms.NotifyIcon { Visible = true, Text = "College Schedule Gadget", Icon = notificationIconImage ?? System.Drawing.SystemIcons.Information };
            
            refreshTimer.Tick += RefreshTimer_Tick; refreshTimer.Start();
            if (string.IsNullOrWhiteSpace(settings.Group)) ShowSetupView(); else ShowScheduleView();
            if (settings.IsBellsMode) { bellsWindow = new BellsWindow(settings); bellsWindow.Show(); }

            _ = LoadGroupsAsync(); _ = LoadWeekCyclesAsync(); _ = LoadTeacherHintsAsync();
            if (!string.IsNullOrWhiteSpace(settings.Group)) _ = LoadScheduleAsync(resetDay: true);
            _ = CheckForUpdatesAsync();
        }

        private async Task LoadTeacherHintsAsync() { try { teacherFullNames = await scheduleService.LoadTeacherHintsAsync(); if (currentSchedule.Count > 0) RenderSelectedDay(); } catch { teacherFullNames = new Dictionary<string, string>(); } }
        private async Task LoadWeekCyclesAsync() { try { weekCycles = await scheduleService.LoadWeekCyclesAsync(); if (!string.IsNullOrWhiteSpace(settings.Group)) _ = LoadScheduleAsync(resetDay: true); } catch { weekCycles = Array.Empty<WeekCycle>(); } }
        private async Task CheckForUpdatesAsync() { try { var update = await UpdateService.CheckAsync(); if (update is not null) { UpdateButton.Visibility = Visibility.Visible; UpdateButton.ToolTip = $"Доступна версія {update.Version}"; UpdateButton.Tag = update.DownloadUrl; } } catch { UpdateButton.Visibility = Visibility.Collapsed; } }
        private void UpdateButton_Click(object sender, RoutedEventArgs e) { if (UpdateButton.Tag is string url && !string.IsNullOrWhiteSpace(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        private void RefreshTimer_Tick(object? sender, EventArgs e) { if (!string.IsNullOrWhiteSpace(settings.Group)) _ = LoadScheduleAsync(true); }
        private async Task LoadGroupsAsync() { try { availableGroups = await scheduleService.LoadGroupsAsync(); } catch { availableGroups = Array.Empty<string>(); } UpdateGroupSuggestions(); }
        private void GroupInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateGroupSuggestions();
        private void UpdateGroupSuggestions() { if (availableGroups.Count == 0 || GroupInput is null) return; var search = GroupInput.Text.Trim(); if (search.Length == 0) { GroupSuggestions.Visibility = Visibility.Collapsed; return; } var matches = availableGroups.Where(group => StartsWithGroupSearch(group, search)).Take(12).ToList(); GroupSuggestions.ItemsSource = matches; GroupSuggestions.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
        private void GroupSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (GroupSuggestions.SelectedItem is not string group) return; GroupInput.Text = group; GroupInput.CaretIndex = GroupInput.Text.Length; GroupSuggestions.SelectedItem = null; GroupSuggestions.Visibility = Visibility.Collapsed; }
        private static bool StartsWithGroupSearch(string group, string search) { if (group.StartsWith(search, StringComparison.OrdinalIgnoreCase)) return true; var ukrainianKeyboardSearch = search.Replace('A', 'А').Replace('a', 'а').Replace('K', 'К').Replace('k', 'к'); return group.StartsWith(ukrainianKeyboardSearch, StringComparison.OrdinalIgnoreCase); }
        
        private async void StartButton_Click(object sender, RoutedEventArgs e) 
        { 
            var group = GroupInput.Text.Trim(); 
            if (group.Length == 0) { SetupStatusText.Text = "Введіть назву групи."; return; } 
            SetupStatusText.Text = "Перевіряю групу..."; 
            try { cachedSchedules.Clear(); currentWeekNumber = GetWeekNumber(selectedDate); var schedule = await scheduleService.LoadGroupAsync(group, currentWeekNumber); cachedSchedules[currentWeekNumber] = schedule; settings.Group = group; SettingsStore.Save(settings); ShowScheduleView(); RenderSchedule(group, schedule); } 
            catch (Exception exception) { SetupStatusText.Text = exception.Message; } 
        }
        
        private async Task LoadScheduleAsync(bool resetDay = false) 
        { 
            var group = settings.Group.Trim(); 
            if (group.Length == 0) { ShowSetupView(); return; } 
            CountdownText.Text = "Завантаження..."; 
            try 
            { 
                if (resetDay) { cachedSchedules.Clear(); selectedDate = DateTime.Today; selectedDayIndex = GetDayIndex(selectedDate); } 
                int targetWeek = GetWeekNumber(selectedDate); 
                if (cachedSchedules.TryGetValue(targetWeek, out var cachedData)) { RenderSchedule(group, cachedData, false); return; }
                var schedule = await scheduleService.LoadGroupAsync(group, targetWeek); cachedSchedules[targetWeek] = schedule; RenderSchedule(group, schedule, false); 
            } 
            catch (Exception exception) { LessonsPanel.Children.Clear(); GroupTitle.Text = "Розклад"; CountdownText.Text = exception.Message.Contains("429") ? "Забагато запитів. Зачекай." : exception.Message; } 
        }
        
        private void SettingsButton_Click(object sender, RoutedEventArgs e) { if (settingsWindow is not null) { settingsWindow.Activate(); return; } settingsWindow = new SettingsWindow(settings, availableGroups) { Owner = this }; settingsWindow.SettingsApplied += SettingsWindow_SettingsApplied; settingsWindow.TestRefreshRequested += SettingsWindow_TestRefreshRequested; settingsWindow.Closed += (_, _) => settingsWindow = null; settingsWindow.Show(); }
        private void SettingsWindow_TestRefreshRequested(object? sender, EventArgs e) => _ = LoadScheduleAsync(true);
        
        private void SettingsWindow_SettingsApplied(object? sender, EventArgs e) 
        { 
            if (settingsWindow is null) return; var groupChanged = !string.Equals(settings.Group, settingsWindow.Settings.Group, StringComparison.OrdinalIgnoreCase); settings = settingsWindow.Settings; SettingsStore.Save(settings); ApplyAppearance(); ApplyWidgetSize(); ApplyStartupSetting(); 
            if (settings.IsBellsMode) { if (bellsWindow == null) { bellsWindow = new BellsWindow(settings); bellsWindow.Show(); } else { bellsWindow.UpdateSettings(settings); } } else if (bellsWindow != null) { bellsWindow.Close(); bellsWindow = null; }
            Visibility = Visibility.Visible; ShowScheduleView(); if (groupChanged) _ = LoadScheduleAsync(resetDay: true); else RenderSelectedDay(); 
        }
        
        private void PinButton_Click(object sender, RoutedEventArgs e) { settings.IsPinned = !settings.IsPinned; UpdatePinButton(); UpdateResizeMode(); SaveWindowSettings(); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        private void UpdateResizeMode() { ResizeMode = settings.IsPinned ? ResizeMode.NoResize : ResizeMode.CanResize; }
        private void ApplyStartupSetting() { try { StartupManager.SetEnabled(settings.StartWithWindows); } catch { } }
        private void UpdatePinButton() { if (PinButton is null) return; PinButton.Opacity = settings.IsPinned ? 1 : 0.65; PinButton.ToolTip = settings.IsPinned ? "Відкріпити" : "Закріпити на місці"; }
        private void MainWindow_Closing(object? sender, CancelEventArgs e) { notificationIcon?.Dispose(); notificationIconImage?.Dispose(); bellsWindow?.Close(); SaveWindowSettings(); }
        private static System.Drawing.Icon? LoadNotificationIcon() { var resourceName = typeof(MainWindow).Assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith("hardcore-heart.png", StringComparison.OrdinalIgnoreCase)); if (resourceName is null) return null; using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream(resourceName); using var bitmap = stream is null ? null : new Bitmap(stream); return bitmap?.GetHicon() is IntPtr iconHandle && iconHandle != IntPtr.Zero ? System.Drawing.Icon.FromHandle(iconHandle) : null; }
        private void SaveWindowSettings() { if (IsLoaded) { settings.Left = Left; settings.Top = Top; settings.Width = Width; settings.Height = Height; } SettingsStore.Save(settings); }
        private void ShowSetupView() { SetupView.Visibility = Visibility.Visible; ScheduleView.Visibility = Visibility.Collapsed; GroupInput.Text = settings.Group; }
        private void ShowScheduleView() { SetupView.Visibility = Visibility.Collapsed; ScheduleView.Visibility = Visibility.Visible; }
        private void RenderSchedule(string group, Dictionary<string, List<ScheduleLesson>> schedule, bool resetDay = false) { currentSchedule = schedule; if (resetDay) { selectedDate = DateTime.Today; selectedDayIndex = GetDayIndex(selectedDate); } currentWeekNumber = GetWeekNumber(selectedDate); RenderSelectedDay(group); }

        private void RenderSelectedDay(string? group = null)
        {
            var currentGroup = group ?? settings.Group;
            if (settings.ShowFullWeek) { RenderFullWeekScheduleColumns(currentGroup); return; }
            LessonsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            LessonsPanel.Orientation = System.Windows.Controls.Orientation.Vertical;
            ApplyWidgetSize(); 
            RenderSingleDaySchedule(currentGroup);
        }

        private void RenderSingleDaySchedule(string currentGroup)
        {
            LessonsPanel.Children.Clear();
            var selectedDay = ScheduleDays[selectedDayIndex];
            var displayDay = char.ToUpper(selectedDay[0]) + selectedDay[1..];
            
            // ПОВЕРНУТО ФОРМАТ: "Група | День тижня (дата)"
            var displayGroup = currentGroup.Length > 9 ? $"{currentGroup[..9]}..." : currentGroup;
            GroupTitle.Text = $"{displayGroup} | {displayDay} ({selectedDate:dd.MM})"; 
            GroupTitle.ToolTip = currentGroup;
            
            SelectedDayText.Text = displayDay;
            StatusText.Text = $"{currentWeekNumber} тиж.";
            todayLessons = currentSchedule.TryGetValue(selectedDay, out var lessons) ? lessons : new List<ScheduleLesson>();

            if (todayLessons.Count == 0)
            {
                LessonsScrollViewer.Visibility = Visibility.Collapsed; EmptyDayPanel.Visibility = Visibility.Visible;
                CountdownText.Text = "Наступні пари будуть пізніше.";
                EmptyDayFace.Text = selectedDayIndex == 6 ? ":3" : ":)"; EmptyDayFace.Foreground = GetAccentBrush();
                EmptyDayMessage.Text = selectedDayIndex == 6 ? "У неділю пар немає" : selectedDayIndex == 5 ? "У суботу пар немає" : "У цей день пар немає";
                return;
            }
            LessonsScrollViewer.Visibility = Visibility.Visible; EmptyDayPanel.Visibility = Visibility.Collapsed;
            AddLessons(todayLessons); UpdateCountdown();
        }

        private void RenderFullWeekScheduleColumns(string currentGroup)
        {
            var displayGroup = currentGroup.Length > 9 ? $"{currentGroup[..9]}..." : currentGroup;
            GroupTitle.Text = $"{displayGroup} | Весь тиждень";
            SelectedDayText.Text = "Пн - Пт"; StatusText.Text = $"{currentWeekNumber} тиж."; CountdownText.Text = "";
            LessonsScrollViewer.Visibility = Visibility.Visible; EmptyDayPanel.Visibility = Visibility.Collapsed; todayLessons.Clear();
            LessonsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; LessonsPanel.Orientation = System.Windows.Controls.Orientation.Horizontal; LessonsPanel.Children.Clear();
            if (!settings.IsPinned) { this.Width = 1150; }
            for (int i = 0; i < 5; i++)
            {
                var dayName = ScheduleDays[i];
                if (currentSchedule.TryGetValue(dayName, out var dayLessons) && dayLessons.Count > 0)
                {
                    var dayColumn = new StackPanel { Width = 200, Margin = new Thickness(0, 0, 20, 0) };
                    dayColumn.Children.Add(new TextBlock { Text = char.ToUpper(dayName[0]) + dayName[1..], Foreground = GetAccentBrush(), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
                    var primaryText = GetPrimaryTextBrush(); var secondaryText = GetSecondaryTextBrush();
                    foreach (var lesson in dayLessons.OrderBy(item => int.TryParse(item.Number, out var number) ? number : 99))
                    {
                        var time = lessonTimes.TryGetValue(lesson.Number, out var lessonTime) ? $" ({lessonTime})" : "";
                        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                        panel.Children.Add(new TextBlock { Text = $"{lesson.Number} пара{time}", Foreground = secondaryText, FontSize = 12 });
                        panel.Children.Add(new TextBlock { Text = lesson.Subject, Foreground = primaryText, FontSize = 14, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
                        panel.Children.Add(new TextBlock { Text = $"{lesson.Teacher} ауд. {lesson.Cabinet}", Foreground = primaryText, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                        dayColumn.Children.Add(panel);
                    }
                    LessonsPanel.Children.Add(dayColumn);
                }
            }
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e) { UpdateCountdown(); UpdateClock(); CheckUpcomingLessonNotifications(); }
        private void UpdateClock() { if (ClockText is not null) ClockText.Text = DateTime.Now.ToString("HH:mm:ss"); }
        private void CheckUpcomingLessonNotifications() { if (settings.DisableNotifications || selectedDate.Date != DateTime.Today) return; var now = DateTime.Now.TimeOfDay; foreach (var lesson in todayLessons) { if (!lessonPeriods.TryGetValue(lesson.Number, out var period)) continue; var key = $"{DateTime.Today:yyyy-MM-dd}:{lesson.Number}"; if (now >= period.Start - TimeSpan.FromMinutes(3) && now < period.Start && notifiedLessons.Add(key)) { notificationIcon?.ShowBalloonTip(5000, "Пара починається через 3 хвилини", $"{lesson.Number} пара: {lesson.Subject}", Forms.ToolTipIcon.Info); } } }

        private void UpdateCountdown()
        {
            if (CountdownText is null || todayLessons.Count == 0 || selectedDate.Date != DateTime.Today || settings.ShowFullWeek) return;
            var now = DateTime.Now.TimeOfDay;
            foreach (var lesson in todayLessons.OrderBy(item => int.TryParse(item.Number, out var number) ? number : 99))
            {
                if (!lessonPeriods.TryGetValue(lesson.Number, out var period)) continue;
                if (now < period.Start) { CountdownText.Text = $"До {lesson.Number} пари: {FormatCountdown(period.Start - now)}"; return; }
                if (now < period.End) { CountdownText.Text = $"Йде {lesson.Number} пара: {FormatCountdown(period.End - now)}"; return; }
            }
            CountdownText.Text = "Пари на сьогодні завершені.";
        }

        private static string FormatCountdown(TimeSpan duration) => duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"mm\:ss");

        private void AddLessons(IEnumerable<ScheduleLesson> lessons)
        {
            var primaryText = GetPrimaryTextBrush(); var secondaryText = GetSecondaryTextBrush();
            int bottomMargin = settings.WidgetStyle == "Cloud" ? 13 : 7;
            if (settings.TextSize == "Small") bottomMargin = settings.WidgetStyle == "Cloud" ? 8 : 3;
            foreach (var lesson in lessons.OrderBy(item => int.TryParse(item.Number, out var number) ? number : 99))
            {
                var time = lessonTimes.TryGetValue(lesson.Number, out var lessonTime) ? $" ({lessonTime})" : "";
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, bottomMargin) };
                panel.Children.Add(new TextBlock { Text = $"{lesson.Number} пара{time}", Foreground = secondaryText, FontSize = GetLessonMetaFontSize() });
                panel.Children.Add(new TextBlock { Text = lesson.Subject, Foreground = primaryText, FontSize = GetLessonSubjectFontSize(), FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
                var teacherText = new TextBlock { Text = $"{lesson.Teacher}    ауд. {lesson.Cabinet}", Foreground = primaryText, FontSize = GetLessonTeacherFontSize(), TextWrapping = TextWrapping.Wrap };
                var fullTeacherName = GetTeacherFullName(lesson.Teacher);
                if (!string.IsNullOrWhiteSpace(fullTeacherName))
                {
                    teacherText.ToolTip = new Border { Background = GetTooltipBackground(), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(55, 255, 255, 255)), BorderThickness = new Thickness(1), Child = new TextBlock { Text = fullTeacherName, Foreground = GetPrimaryTextBrush(), FontSize = 12 } };
                    ToolTipService.SetInitialShowDelay(teacherText, 180);
                }
                panel.Children.Add(teacherText); LessonsPanel.Children.Add(panel);
            }
            if (settings.WidgetStyle == "Cloud" && !settings.IsPinned) { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { UpdateLayout(); Height = Math.Clamp(LessonsPanel.ActualHeight + 150, MinHeight, 430); })); }
        }

        private string GetTeacherFullName(string teacher) { var names = teacher.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); var fullNames = names.Select(name => teacherFullNames.TryGetValue(NormalizeTeacherKey(name), out var full) ? full : name); return string.Join(", ", fullNames.Where(name => !string.IsNullOrWhiteSpace(name))); }
        private static string NormalizeTeacherKey(string value) { var result = value.ToUpperInvariant().Replace('A', 'А').Replace('B', 'В').Replace('C', 'С').Replace('E', 'Е').Replace('H', 'Н').Replace('I', 'І').Replace('K', 'К').Replace('M', 'М').Replace('O', 'О').Replace('P', 'Р').Replace('T', 'Т').Replace('X', 'Х'); return new string(result.Where(character => !char.IsWhiteSpace(character) && character != '.').ToArray()); }

        private void ApplyAppearance()
        {
            System.Windows.Media.Color bgColor;
            if (!string.IsNullOrWhiteSpace(settings.CustomColor)) { try { var baseColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.CustomColor); bgColor = System.Windows.Media.Color.FromArgb(0xC0, baseColor.R, baseColor.G, baseColor.B); } catch { bgColor = System.Windows.Media.Color.FromArgb(0xC0, 0x14, 0x14, 0x14); } }
            else { var backgroundHex = settings.Theme switch { "Violet" => "#C026163D", "Ember" => "#C03D2118", "Graphite" => "#C02A2E35", "Cyberpunk" => "#E6101018", "Matcha" => "#C0202A22", "Ocean" => "#C00B1B2E", _ => "#C0141414" }; bgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backgroundHex); }
            ScheduleCard.Opacity = settings.Opacity; ScheduleCard.Background = new SolidColorBrush(bgColor); ScheduleCard.CornerRadius = settings.WidgetStyle == "Cloud" ? new CornerRadius(22) : new CornerRadius(12);
            int basePadding = settings.WidgetStyle == "Cloud" ? 18 : 10; if (settings.TextSize == "Small") basePadding = settings.WidgetStyle == "Cloud" ? 12 : 5; ScheduleCard.Padding = new Thickness(basePadding);
            GroupTitle.Foreground = GetPrimaryTextBrush(); StatusText.Foreground = GetSecondaryTextBrush(); SelectedDayText.Foreground = GetSecondaryTextBrush(); CountdownText.Foreground = GetAccentBrush();
            if (currentSchedule.Count > 0) RenderSelectedDay();
        }

        private System.Windows.Media.Brush GetPrimaryTextBrush() => settings.Theme switch { "Graphite" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 246, 248)), "Matcha" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(235, 245, 235)), "Ocean" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 240, 255)), "Cyberpunk" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 255)), _ => System.Windows.Media.Brushes.White };
        private System.Windows.Media.Brush GetSecondaryTextBrush() => settings.Theme switch { "Violet" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 202, 255)), "Ember" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 218, 192)), "Graphite" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(195, 202, 210)), "Cyberpunk" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 40, 130)), "Matcha" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(165, 205, 175)), "Ocean" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 195, 235)), _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(153, 255, 255, 255)) };
        private System.Windows.Media.Brush GetAccentBrush() => settings.Theme switch { "Violet" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 165, 255)), "Ember" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 178, 105)), "Graphite" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(144, 198, 255)), "Cyberpunk" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 240, 255)), "Matcha" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 235, 150)), "Ocean" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 220, 255)), _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 217, 255)) };
        private System.Windows.Media.Brush GetTooltipBackground() => settings.Theme switch { "Violet" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 34, 70)), "Ember" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 39, 25)), "Graphite" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 48, 56)), "Cyberpunk" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 10, 40)), "Matcha" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 45, 35)), "Ocean" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 35, 55)), _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 48, 56)) };

        private void ApplyWidgetSize() { if (settings.IsPinned) return; var size = settings.TextSize switch { "Small" => (Width: 280d, Height: 320d), "Large" => (Width: 420d, Height: 520d), _ => (Width: 340d, Height: 430d) }; Width = size.Width; Height = size.Height; }
        private double GetLessonMetaFontSize() => settings.TextSize switch { "Small" => 11, "Large" => 14, _ => 12 };
        private double GetLessonSubjectFontSize() => settings.TextSize switch { "Small" => 13, "Large" => 17, _ => 14 };
        private double GetLessonTeacherFontSize() => settings.TextSize switch { "Small" => 12, "Large" => 15, _ => 13 };
        private static int GetDayIndex(DateTime date) => date.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)date.DayOfWeek - 1;
        private int GetWeekNumber(DateTime date) { var cycle = weekCycles.LastOrDefault(item => date.Date >= item.Start.Date); return cycle?.Number ?? GetAcademicWeekNumber(date); }
        private static int GetAcademicWeekNumber(DateTime date) { var academicStart = new DateTime(date.Month >= 9 ? date.Year : date.Year - 1, 9, 1); var firstMonday = academicStart.AddDays(((int)DayOfWeek.Monday - (int)academicStart.DayOfWeek + 7) % 7); if (date.Date < firstMonday.Date) return 1; var week = 2 + (int)Math.Floor((date.Date - firstMonday.Date).TotalDays / 7); return ((week - 1) % 4) + 1; }

        private void PreviousDayButton_Click(object sender, RoutedEventArgs e) { selectedDate = selectedDate.AddDays(-1); selectedDayIndex = GetDayIndex(selectedDate); _ = LoadScheduleAsync(); }
        private void NextDayButton_Click(object sender, RoutedEventArgs e) { selectedDate = selectedDate.AddDays(1); selectedDayIndex = GetDayIndex(selectedDate); _ = LoadScheduleAsync(); }

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
        private void MainWindow_StateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; var hwnd = new WindowInteropHelper(this).Handle; SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW); } }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (settings.IsPinned) { e.Handled = true; return; } try { this.DragMove(); } catch { } finally { SaveWindowSettings(); } }
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) { if (!settings.IsPinned && IsLoaded && !settings.ShowFullWeek) SaveWindowSettings(); }
    }
}