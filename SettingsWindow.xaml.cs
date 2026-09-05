using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace CollegeScheduleGadget;

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
            Group = settings.Group,
            Opacity = settings.Opacity,
            Left = settings.Left,
            Top = settings.Top,
            Width = settings.Width,
            Height = settings.Height,
            IsPinned = settings.IsPinned,
            StartWithWindows = settings.StartWithWindows,
            DisableNotifications = settings.DisableNotifications,
            Theme = settings.Theme,
            WidgetStyle = settings.WidgetStyle,
            TextSize = settings.TextSize,
            CustomColor = settings.CustomColor
        };
        availableGroups = groups;
        GroupInput.Text = Settings.Group;
        OpacitySlider.Value = Math.Clamp(Settings.Opacity, 0.25, 1.0);
        ThemeComboBox.SelectedValue = Settings.Theme;
        StyleComboBox.SelectedValue = Settings.WidgetStyle;
        TextSizeSelector.SelectedValue = Settings.TextSize;
        ColorPreview.Tag = Settings.CustomColor;
        ColorPreview.Background = GetCustomColorBrush();
        StartWithWindowsCheckBox.IsChecked = Settings.StartWithWindows;
        DisableNotificationsCheckBox.IsChecked = Settings.DisableNotifications;
        ShowPanel(GeneralPanel);
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

    private void OpacitySlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        var position = e.GetPosition(slider);
        var ratio = Math.Clamp(position.X / slider.ActualWidth, 0, 1);
        slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        e.Handled = true;
    }

    private void GeneralTab_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(GeneralPanel);
    }

    private void ScheduleTab_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(SchedulePanel);
    }

    private void AboutTab_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(AboutPanel);
    }

    private void ShowPanel(UIElement selectedPanel)
    {
        var generalButton = FindName("GeneralTabButton") as System.Windows.Controls.Button;
        var scheduleButton = FindName("ScheduleTabButton") as System.Windows.Controls.Button;
        var aboutButton = FindName("AboutTabButton") as System.Windows.Controls.Button;
        GeneralPanel.Visibility = selectedPanel == GeneralPanel ? Visibility.Visible : Visibility.Collapsed;
        SchedulePanel.Visibility = selectedPanel == SchedulePanel ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = selectedPanel == AboutPanel ? Visibility.Visible : Visibility.Collapsed;
        generalButton!.Background = selectedPanel == GeneralPanel
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 76, 84))
            : System.Windows.Media.Brushes.Transparent;
        scheduleButton!.Background = selectedPanel == SchedulePanel
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 76, 84))
            : System.Windows.Media.Brushes.Transparent;
        aboutButton!.Background = selectedPanel == AboutPanel
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 76, 84))
            : System.Windows.Media.Brushes.Transparent;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupInput.Text))
        {
            System.Windows.MessageBox.Show("Введіть групу.", "Налаштування",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Settings.Group = GroupInput.Text.Trim();
        Settings.Opacity = OpacitySlider.Value;
        Settings.Theme = (ThemeComboBox.SelectedValue as string) ?? "Midnight";
        Settings.WidgetStyle = (StyleComboBox.SelectedValue as string) ?? "Minimalism";
        Settings.TextSize = (TextSizeSelector.SelectedValue as string) ?? "Medium";
        Settings.CustomColor = ColorPreview.Tag as string ?? "";
        Settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        Settings.DisableNotifications = DisableNotificationsCheckBox.IsChecked == true;
        SettingsApplied?.Invoke(this, EventArgs.Empty);
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

    private void TestRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        TestRefreshRequested?.Invoke(this, EventArgs.Empty);
        UpdateStatusText.Text = "Оновлення розкладу запущено.";
    }

    private void CustomColorButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            AllowFullOpen = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        Settings.CustomColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        ColorPreview.Tag = Settings.CustomColor;
        ColorPreview.Background = GetCustomColorBrush();
    }

    private System.Windows.Media.Brush GetCustomColorBrush()
    {
        if (ColorPreview.Tag is not string value || string.IsNullOrWhiteSpace(value))
        {
            return System.Windows.Media.Brushes.Transparent;
        }

        try
        {
            return new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
        }
        catch
        {
            return System.Windows.Media.Brushes.Transparent;
        }
    }

    private System.Windows.Controls.ComboBox TextSizeSelector =>
        (System.Windows.Controls.ComboBox)FindName("TextSizeComboBox")!;

    private Border ColorPreview =>
        (Border)FindName("CustomColorPreview")!;

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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

    private void TgSupportButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://t.me/Denysyoyo2010") { UseShellExecute = true });
    }

    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://send.monobank.ua/jar/5yiazeNfMz") { UseShellExecute = true });
    }
}