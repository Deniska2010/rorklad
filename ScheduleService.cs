using System.Net.Http;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CollegeScheduleGadget;

public sealed class ScheduleLesson
{
    public string Cabinet { get; set; } = "";
    public string Number { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Teacher { get; set; } = "";
    public string Week { get; set; } = "";
}

public sealed record WeekCycle(int Number, DateTime Start);

public sealed class ScheduleService
{
    private const string ScheduleUrl = "https://kep.nung.edu.ua/pages/education/schedule";
    private const string HintsUrl = "https://kep.nung.edu.ua/api/exam-hints";
    private static readonly HttpClient Client = new();

    public async Task<IReadOnlyList<WeekCycle>> LoadWeekCyclesAsync(
        CancellationToken cancellationToken = default)
    {
        var page = await Client.GetStringAsync(ScheduleUrl, cancellationToken);
        var matches = Regex.Matches(page,
            @"number:(\d+),startDate:`(\d{2}\.\d{2}\.\d{4})`",
            RegexOptions.CultureInvariant);
        var cycles = new List<WeekCycle>();
        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out var number)
                && DateTime.TryParseExact(match.Groups[2].Value, "dd.MM.yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            {
                cycles.Add(new WeekCycle(number, start.Date));
            }
        }

        return cycles
            .GroupBy(cycle => cycle.Number)
            .Select(group => group.First())
            .OrderBy(cycle => cycle.Start)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadTeacherHintsAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await Client.GetStringAsync(HintsUrl, cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("teachers", out var teachers)
            || teachers.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return teachers.EnumerateObject()
            .Where(item => item.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(item => item.Name, item => item.Value.GetString() ?? "");
    }

    public async Task<IReadOnlyList<string>> LoadGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var groups = await LoadGroupsDictionaryAsync(cancellationToken);
        return groups.Keys.OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Dictionary<string, List<ScheduleLesson>>> LoadGroupAsync(
        string group, int weekNumber, CancellationToken cancellationToken = default)
    {
        var groups = await LoadGroupsDictionaryAsync(cancellationToken);

        var matchingGroup = groups.Keys.FirstOrDefault(key =>
            string.Equals(key, group.Trim(), StringComparison.OrdinalIgnoreCase));
        if (matchingGroup is null)
        {
            throw new KeyNotFoundException($"Групу «{group}» не знайдено на сайті.");
        }

        return groups[matchingGroup]
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Where(lesson => IsLessonInWeek(lesson.Week, weekNumber)).ToList());
    }

    private static async Task<Dictionary<string, Dictionary<string, List<ScheduleLesson>>>>
        LoadGroupsDictionaryAsync(CancellationToken cancellationToken)
    {
        var page = await Client.GetStringAsync(ScheduleUrl, cancellationToken);
        var data = ExtractScheduleData(page);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<ScheduleLesson>>>>(data,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Сайт не повернув дані розкладу.");
    }

    private static bool IsLessonInWeek(string? week, int weekNumber)
    {
        if (string.IsNullOrWhiteSpace(week) || week.Contains("усі", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var part in week.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 1 && int.TryParse(range[0], out var single) && single == weekNumber)
            {
                return true;
            }

            if (range.Length == 2 && int.TryParse(range[0], out var first)
                && int.TryParse(range[1], out var last)
                && weekNumber >= first && weekNumber <= last)
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractScheduleData(string page)
    {
        const string marker = "let scheduleData=normalizeScheduleGroups(";
        var markerIndex = page.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("Не знайдено блок даних розкладу на сайті.");
        }

        var objectStart = page.IndexOf('{', markerIndex + marker.Length);
        var objectEnd = FindObjectEnd(page, objectStart);
        var javascriptObject = page[objectStart..(objectEnd + 1)];

        var json = Regex.Replace(javascriptObject, "`([^`]*)`", match =>
            JsonSerializer.Serialize(match.Groups[1].Value), RegexOptions.Singleline);
        json = Regex.Replace(json, @"([,{]\s*)([\p{L}\p{N}_'-]+)\s*:", "$1\"$2\":");
        return json;
    }

    private static int FindObjectEnd(string text, int start)
    {
        var depth = 0;
        var inBacktick = false;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && (inString || inBacktick))
            {
                escaped = true;
                continue;
            }

            if (character == '"' && !inBacktick)
            {
                inString = !inString;
                continue;
            }

            if (character == '`' && !inString)
            {
                inBacktick = !inBacktick;
                continue;
            }

            if (inString || inBacktick)
            {
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                return index;
            }
        }

        throw new InvalidOperationException("Блок даних розкладу пошкоджений.");
    }
}
