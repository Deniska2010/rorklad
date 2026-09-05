using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
        private readonly DispatcherTimer countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        private readonly DispatcherTimer refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(10)
        };
        private readonly ScheduleService scheduleService = new ScheduleService();
        private IReadOnlyList<string> availableGroups = Array.Empty<string>();
        private AppSettings settings = new AppSettings();
        private List<ScheduleLesson> todayLessons = new();
        private Dictionary<string, List<ScheduleLesson>> currentSchedule = new();
        private int selectedDayIndex;
        private static readonly string[] ScheduleDays =
        {
            "понеділок", "вівторок", "середа", "четвер", "п'ятниця", "субота", "неділя"
        };
        private readonly Dictionary<string, string> lessonTimes = new()
        {
            ["1"] = "08:00 - 09:20",
            ["2"] = "09:30 - 10:50",
            ["3"] = "11:10 - 12:30",
            ["4"] = "12:40 - 14:00",
            ["5"] = "14:10 - 15:30",
            ["6"] = "15:40 - 17:00"
        };
        private readonly Dictionary<string, (TimeSpan Start, TimeSpan End)> lessonPeriods = new()
        {
            ["1"] = (new TimeSpan(8, 0, 0), new TimeSpan(9, 20, 0)),
            ["2"] = (new TimeSpan(9, 30, 0), new TimeSpan(10, 50, 0)),
            ["3"] = (new TimeSpan(11, 10, 0), new TimeSpan(12, 30, 0)),
            ["4"] = (new TimeSpan(12, 40, 0), new TimeSpan(14, 0, 0)),
            ["5"] = (new TimeSpan(14, 10, 0), new TimeSpan(15, 30, 0)),
            ["6"] = (new TimeSpan(15, 40, 0), new TimeSpan(17, 0, 0))
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

            settings = SettingsStore.Load();
            this.Left = settings.Left ?? 100;
            this.Top = settings.Top ?? 100;
            this.Width = settings.Width ?? 340;
            this.Height = settings.Height ?? 430;
            ScheduleCard.Opacity = settings.Opacity;
            ApplyAppearance();
            UpdatePinButton();
            UpdateResizeMode();
            ApplyStartupSetting();
            selectedDayIndex = GetCurrentDayIndex();
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            desktopTimer.Tick += DesktopTimer_Tick;
            desktopTimer.Start();
            countdownTimer.Tick += CountdownTimer_Tick;
            countdownTimer.Start();
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();
            if (string.IsNullOrWhiteSpace(settings.Group))
            {
                ShowSetupView();
            }
            else
            {
                ShowScheduleView();
            }

            _ = LoadGroupsAsync();
            if (!string.IsNullOrWhiteSpace(settings.Group))
            {
                _ = LoadScheduleAsync(resetDay: true);
            }
            _ = CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var update = await UpdateService.CheckAsync();
                if (update is not null)
                {
                    UpdateButton.Visibility = Visibility.Visible;
                    UpdateButton.ToolTip = $"Доступна версія {update.Version}";
                    UpdateButton.Tag = update.DownloadUrl;
                }
            }
            catch
            {
                UpdateButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateButton.Tag is not string url || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(settings.Group))
            {
                _ = LoadScheduleAsync();
            }
        }

        private async Task LoadGroupsAsync()
        {
            try
            {
                availableGroups = await scheduleService.LoadGroupsAsync();
            }
            catch
            {
                availableGroups = Array.Empty<string>();
            }

            UpdateGroupSuggestions();
        }

        private void GroupInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateGroupSuggestions();
        }

        private void UpdateGroupSuggestions()
        {
            if (availableGroups.Count == 0 || GroupInput is null)
            {
                return;
            }

            var search = GroupInput.Text.Trim();
            if (search.Length == 0)
            {
                GroupSuggestions.Visibility = Visibility.Collapsed;
                return;
            }

            var matches = availableGroups
                .Where(group => StartsWithGroupSearch(group, search))
                .Take(12)
                .ToList();
            GroupSuggestions.ItemsSource = matches;
            GroupSuggestions.Visibility = matches.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void GroupSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupSuggestions.SelectedItem is not string group)
            {
                return;
            }

            GroupInput.Text = group;
            GroupInput.CaretIndex = GroupInput.Text.Length;
            GroupSuggestions.SelectedItem = null;
            GroupSuggestions.Visibility = Visibility.Collapsed;
        }

        private static bool StartsWithGroupSearch(string group, string search)
        {
            if (group.StartsWith(search, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var ukrainianKeyboardSearch = search
                .Replace('A', 'А').Replace('a', 'а')
                .Replace('K', 'К').Replace('k', 'к');
            return group.StartsWith(ukrainianKeyboardSearch, StringComparison.OrdinalIgnoreCase);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var group = GroupInput.Text.Trim();
            if (group.Length == 0)
            {
                SetupStatusText.Text = "Введіть назву групи.";
                return;
            }

            SetupStatusText.Text = "Перевіряю групу...";
            try
            {
                var schedule = await scheduleService.LoadGroupAsync(group, 1);
                settings.Group = group;
                SettingsStore.Save(settings);
                ShowScheduleView();
                RenderSchedule(group, schedule);
            }
            catch (Exception exception)
            {
                SetupStatusText.Text = exception.Message;
            }
        }

        private async Task LoadScheduleAsync(bool resetDay = false)
        {
            var group = settings.Group.Trim();
            if (group.Length == 0)
            {
                ShowSetupView();
                return;
            }

            StatusText.Text = "Завантаження розкладу...";
            CountdownText.Text = "";
            try
            {
                var schedule = await scheduleService.LoadGroupAsync(group, 1);
                RenderSchedule(group, schedule, resetDay);
            }
            catch (Exception exception)
            {
                LessonsPanel.Children.Clear();
                GroupTitle.Text = "Розклад";
                StatusText.Text = exception.Message;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(settings, availableGroups)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() != true)
            {
                return;
            }

            var groupChanged = !string.Equals(settings.Group, settingsWindow.Settings.Group,
                StringComparison.OrdinalIgnoreCase);
            settings = settingsWindow.Settings;
            SettingsStore.Save(settings);
            ScheduleCard.Opacity = settings.Opacity;
            ApplyAppearance();
            ApplyStartupSetting();

            if (groupChanged)
            {
                _ = LoadScheduleAsync(resetDay: true);
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            settings.IsPinned = !settings.IsPinned;
            UpdatePinButton();
            UpdateResizeMode();
            SaveWindowSettings();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UpdateResizeMode()
        {
            ResizeMode = settings.IsPinned ? ResizeMode.NoResize : ResizeMode.CanResize;
        }

        private void ApplyStartupSetting()
        {
            try
            {
                StartupManager.SetEnabled(settings.StartWithWindows);
            }
            catch
            {
                // Autostart is optional and must not prevent the widget from opening.
            }
        }

        private void UpdatePinButton()
        {
            if (PinButton is null)
            {
                return;
            }

            PinButton.Opacity = settings.IsPinned ? 1 : 0.65;
            PinButton.ToolTip = settings.IsPinned ? "Відкріпити" : "Закріпити на місці";
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            SaveWindowSettings();
        }

        private void SaveWindowSettings()
        {
            if (IsLoaded)
            {
                settings.Left = Left;
                settings.Top = Top;
                settings.Width = Width;
                settings.Height = Height;
            }

            SettingsStore.Save(settings);
        }

        private void ShowSetupView()
        {
            SetupView.Visibility = Visibility.Visible;
            ScheduleView.Visibility = Visibility.Collapsed;
            Height = 320;
            GroupInput.Text = settings.Group;
        }

        private void ShowScheduleView()
        {
            SetupView.Visibility = Visibility.Collapsed;
            ScheduleView.Visibility = Visibility.Visible;
            Height = 430;
        }

        private void RenderSchedule(string group, Dictionary<string, List<ScheduleLesson>> schedule,
            bool resetDay = false)
        {
            currentSchedule = schedule;
            if (resetDay)
            {
                selectedDayIndex = GetCurrentDayIndex();
            }
            RenderSelectedDay(group);
        }

        private void RenderSelectedDay(string? group = null)
        {
            LessonsPanel.Children.Clear();
            var currentGroup = group ?? settings.Group;
            var selectedDay = ScheduleDays[selectedDayIndex];
            GroupTitle.Text = $"{currentGroup} | {selectedDay}";
            SelectedDayText.Text = selectedDay;

            todayLessons = currentSchedule.TryGetValue(selectedDay, out var lessons)
                ? lessons
                : new List<ScheduleLesson>();

            var isToday = selectedDayIndex == GetCurrentDayIndex();
            if (todayLessons.Count == 0)
            {
                StatusText.Text = isToday ? "Сьогодні пар немає." : "У цей день пар немає.";
                CountdownText.Text = isToday
                    ? "Наступні пари будуть у наступний навчальний день."
                    : "";
                return;
            }

            StatusText.Text = isToday ? "Поточний тиждень" : "Розклад на вибраний день";
            CountdownText.Text = "";
            AddLessons(todayLessons);
            UpdateCountdown();
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            UpdateCountdown();
        }

        private void UpdateCountdown()
        {
            if (CountdownText is null || todayLessons.Count == 0
                || selectedDayIndex != GetCurrentDayIndex())
            {
                return;
            }

            var now = DateTime.Now.TimeOfDay;
            foreach (var lesson in todayLessons.OrderBy(item => int.TryParse(item.Number, out var number) ? number : 99))
            {
                if (!lessonPeriods.TryGetValue(lesson.Number, out var period))
                {
                    continue;
                }

                if (now < period.Start)
                {
                    CountdownText.Text = $"До {lesson.Number} пари: {FormatCountdown(period.Start - now)}";
                    return;
                }

                if (now < period.End)
                {
                    CountdownText.Text = $"Зараз {lesson.Number} пара · до кінця: {FormatCountdown(period.End - now)}";
                    return;
                }
            }

            CountdownText.Text = "Пари на сьогодні завершені.";
        }

        private static string FormatCountdown(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"mm\:ss");
        }

        private void AddLessons(IEnumerable<ScheduleLesson> lessons)
        {
            var primaryText = GetPrimaryTextBrush();
            var secondaryText = GetSecondaryTextBrush();
            foreach (var lesson in lessons.OrderBy(item => int.TryParse(item.Number, out var number) ? number : 99))
            {
                var time = lessonTimes.TryGetValue(lesson.Number, out var lessonTime)
                    ? $" ({lessonTime})"
                    : "";
                var margin = settings.WidgetStyle switch
                {
                    "Airy" => new Thickness(0, 0, 0, 16),
                    "Compact" => new Thickness(0, 0, 0, 4),
                    _ => new Thickness(0, 0, 0, 10)
                };
                var panel = new StackPanel { Margin = margin };
                panel.Children.Add(new TextBlock
                {
                    Text = $"{lesson.Number} пара{time}",
                    Foreground = secondaryText,
                    FontSize = 12
                });
                panel.Children.Add(new TextBlock
                {
                    Text = lesson.Subject,
                    Foreground = primaryText,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium,
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(new TextBlock
                {
                    Text = $"{lesson.Teacher}    ауд. {lesson.Cabinet}",
                    Foreground = primaryText,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                });
                LessonsPanel.Children.Add(panel);
            }
        }

        private void ApplyAppearance()
        {
            var background = settings.Theme switch
            {
                "Violet" => "#C026163D",
                "Ember" => "#C03D2118",
                "Graphite" => "#C02A2E35",
                _ => "#C0141414"
            };

            ScheduleCard.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(background));
            ScheduleCard.CornerRadius = settings.WidgetStyle switch
            {
                "Airy" => new CornerRadius(18),
                "Compact" => new CornerRadius(8),
                _ => new CornerRadius(12)
            };
            ScheduleCard.Padding = settings.WidgetStyle switch
            {
                "Airy" => new Thickness(20),
                "Compact" => new Thickness(10),
                _ => new Thickness(15)
            };
            GroupTitle.Foreground = GetPrimaryTextBrush();
            StatusText.Foreground = GetSecondaryTextBrush();
            SelectedDayText.Foreground = GetSecondaryTextBrush();
            CountdownText.Foreground = GetAccentBrush();
            if (currentSchedule.Count > 0)
            {
                RenderSelectedDay();
            }
        }

        private Brush GetPrimaryTextBrush()
        {
            return settings.Theme == "Graphite"
                ? new SolidColorBrush(Color.FromRgb(244, 246, 248))
                : Brushes.White;
        }

        private Brush GetSecondaryTextBrush()
        {
            return settings.Theme switch
            {
                "Violet" => new SolidColorBrush(Color.FromRgb(218, 202, 255)),
                "Ember" => new SolidColorBrush(Color.FromRgb(255, 218, 192)),
                "Graphite" => new SolidColorBrush(Color.FromRgb(195, 202, 210)),
                _ => new SolidColorBrush(Color.FromArgb(153, 255, 255, 255))
            };
        }

        private Brush GetAccentBrush()
        {
            return settings.Theme switch
            {
                "Violet" => new SolidColorBrush(Color.FromRgb(211, 165, 255)),
                "Ember" => new SolidColorBrush(Color.FromRgb(255, 178, 105)),
                "Graphite" => new SolidColorBrush(Color.FromRgb(144, 198, 255)),
                _ => new SolidColorBrush(Color.FromRgb(102, 217, 255))
            };
        }

        private static string GetUkrainianDayName(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "понеділок",
                DayOfWeek.Tuesday => "вівторок",
                DayOfWeek.Wednesday => "середа",
                DayOfWeek.Thursday => "четвер",
                DayOfWeek.Friday => "п'ятниця",
                DayOfWeek.Saturday => "субота",
                _ => "неділя"
            };
        }

        private static int GetCurrentDayIndex()
        {
            return DateTime.Now.DayOfWeek == DayOfWeek.Sunday
                ? 6
                : (int)DateTime.Now.DayOfWeek - 1;
        }

        private void PreviousDayButton_Click(object sender, RoutedEventArgs e)
        {
            selectedDayIndex = (selectedDayIndex + ScheduleDays.Length - 1) % ScheduleDays.Length;
            RenderSelectedDay();
        }

        private void NextDayButton_Click(object sender, RoutedEventArgs e)
        {
            selectedDayIndex = (selectedDayIndex + 1) % ScheduleDays.Length;
            RenderSelectedDay();
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
            if (settings.IsPinned)
            {
                e.Handled = true;
                return;
            }

            try
            {
                this.DragMove();
            }
            finally
            {
                SaveWindowSettings();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!settings.IsPinned && IsLoaded)
            {
                SaveWindowSettings();
            }
        }
    }
}