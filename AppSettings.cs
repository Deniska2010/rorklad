using System;
using System.IO;
using System.Text.Json;

namespace CollegeScheduleGadget;

public sealed class AppSettings
{
    public string Group { get; set; } = "";
    public double Opacity { get; set; } = 0.88;
    
    // Координати головного вікна (розклад)
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool IsPinned { get; set; }
    
    // Координати вікна дзвінків
    public double? BellsLeft { get; set; }
    public double? BellsTop { get; set; }
    public double? BellsWidth { get; set; }
    public double? BellsHeight { get; set; }
    public bool IsBellsPinned { get; set; }

    public bool StartWithWindows { get; set; }
    public bool DisableNotifications { get; set; } = false;
    
    public string Theme { get; set; } = "Midnight";
    public string WidgetStyle { get; set; } = "Minimalism";
    public string TextSize { get; set; } = "Medium";
    public string CustomColor { get; set; } = "";
    public bool ShowFullWeek { get; set; } = false;

    public bool IsBellsMode { get; set; } = false;
    public bool UseShortenedBells { get; set; } = false;
    public string BellsTheme { get; set; } = "Cyberpunk";
}

public static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CollegeScheduleGadget",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}