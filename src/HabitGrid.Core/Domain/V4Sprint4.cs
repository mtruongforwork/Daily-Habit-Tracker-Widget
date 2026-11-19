using System.Text.Json;

namespace HabitGrid.Core.Domain;

/// <summary>Mẫu câu hỏi phản tư có hướng dẫn (Guided Reflection Prompt) cho trang Nhật ký.</summary>
public sealed record JournalPromptItem(string Id, string CategoryKey, string TextKey);

public static class JournalPrompts
{
    public static IReadOnlyList<JournalPromptItem> All { get; } =
    [
        new("gratitude", "journal.prompt.cat.gratitude", "journal.prompt.gratitude"),
        new("win", "journal.prompt.cat.win", "journal.prompt.win"),
        new("lesson", "journal.prompt.cat.lesson", "journal.prompt.lesson"),
        new("energy", "journal.prompt.cat.energy", "journal.prompt.energy"),
        new("tomorrow", "journal.prompt.cat.tomorrow", "journal.prompt.tomorrow")
    ];

    /// <summary>Trả về danh sách gợi ý với câu nổi bật xoay vòng theo ngày.</summary>
    public static IReadOnlyList<JournalPromptItem> GetForDay(DateOnly day)
    {
        if (All.Count == 0)
        {
            return [];
        }

        var offset = Math.Abs(day.DayNumber) % All.Count;
        var ordered = new List<JournalPromptItem>(All.Count);
        for (var i = 0; i < All.Count; i++)
        {
            ordered.Add(All[(offset + i) % All.Count]);
        }

        return ordered;
    }
}

/// <summary>Quy tắc Habit Stacking (Plus): "Sau khi xong A → gợi ý làm B".</summary>
public sealed record HabitStackRule(
    Guid Id,
    Guid TriggerHabitId,
    Guid NextHabitId,
    string? CueNote = null);

public sealed record HabitStackSuggestion(Habit Trigger, Habit Next, string? CueNote);

public static class HabitStackRules
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<HabitStackRule> ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<HabitStackRule>>(json, JsonOptions);
            return list?
                .Where(r => r.TriggerHabitId != Guid.Empty && r.NextHabitId != Guid.Empty && r.TriggerHabitId != r.NextHabitId)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string SerializeRules(IReadOnlyList<HabitStackRule> rules) =>
        JsonSerializer.Serialize(rules, JsonOptions);

    /// <summary>
    /// Khi thói quen <paramref name="completedHabitId"/> vừa hoàn thành, tìm thói quen kế tiếp B chưa xong hôm nay.
    /// </summary>
    public static HabitStackSuggestion? FindSuggestion(
        Guid completedHabitId,
        IReadOnlyList<HabitStackRule> rules,
        IReadOnlyDictionary<Guid, bool> doneByHabitId,
        IReadOnlyDictionary<Guid, Habit> habitsById)
    {
        if (!habitsById.TryGetValue(completedHabitId, out var trigger))
        {
            return null;
        }

        foreach (var rule in rules)
        {
            if (rule.TriggerHabitId != completedHabitId || rule.NextHabitId == completedHabitId)
            {
                continue;
            }

            if (!habitsById.TryGetValue(rule.NextHabitId, out var next))
            {
                continue;
            }

            if (doneByHabitId.TryGetValue(next.Id, out var isDone) && isDone)
            {
                continue;
            }

            return new HabitStackSuggestion(trigger, next, rule.CueNote);
        }

        return null;
    }
}
