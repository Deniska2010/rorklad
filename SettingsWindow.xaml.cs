using System.Windows;
using System.Windows.Controls;

namespace CollegeScheduleGadget;

public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<string> availableGroups;
    public AppSettings Settings { get; }

    public SettingsWindow(AppSettings settings, IReadOnlyList<string> groups)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            Group = settings.Group,
            Opacity = settings.Opacity,
            Left = settings.Left,
            Top = settings.Top,
            Width = settings.Width,
            Height = settings.Height,
            IsPinned = settings.IsPinned,
            StartWithWindows = settings.StartWithWindows,
            Theme = settings.Theme,
            WidgetStyle = settings.WidgetStyle
        };
        availableGroups = groups;
        GroupInput.Text = Settings.Group;
        OpacitySlider.Value = Settings.Opacity;
        ThemeComboBox.SelectedValue = Settings.Theme;
        StyleComboBox.SelectedValue = Settings.WidgetStyle;
        StartWithWindowsCheckBox.IsChecked = Settings.StartWithWindows;
    }

    private void GroupInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var search = GroupInput.Text.Trim();
        if (search.Length == 0)
        {
            GroupSuggestions.Visibility = Visibility.Collapsed;
            return;
        }

        var matches = availableGroups
            .Where(group => StartsWithGroupSearch(group, search))
            .Take(10)
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

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue is not null)
        {
            OpacityValue.Text = $"{Math.Round(e.NewValue * 100)}%";
        }
    }

    private void GeneralTab_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(GeneralPanel);
    }

    private void ScheduleTab_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(SchedulePanel);
    }

    private void AppearanceTab_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(AppearancePanel);
    }

    private void AboutTab_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(AboutPanel);
    }

    private void ShowPanel(UIElement selectedPanel)
    {
        GeneralPanel.Visibility = selectedPanel == GeneralPanel ? Visibility.Visible : Visibility.Collapsed;
        SchedulePanel.Visibility = selectedPanel == SchedulePanel ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = selectedPanel == AppearancePanel ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = selectedPanel == AboutPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupInput.Text))
        {
            MessageBox.Show("Введіть групу.", "Налаштування", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Settings.Group = GroupInput.Text.Trim();
        Settings.Opacity = OpacitySlider.Value;
        Settings.Theme = (ThemeComboBox.SelectedValue as string) ?? "Midnight";
        Settings.WidgetStyle = (StyleComboBox.SelectedValue as string) ?? "Minimalism";
        Settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Перевіряю оновлення...";
        try
        {
            var update = await UpdateService.CheckAsync();
            UpdateStatusText.Text = update is null
                ? $"Оновлень немає. Поточна версія {UpdateService.CurrentVersion}."
                : $"Доступна версія {update.Version}. Натисніть кнопку оновлення біля pin.";
        }
        catch
        {
            UpdateStatusText.Text = "Не вдалося перевірити оновлення.";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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
}
