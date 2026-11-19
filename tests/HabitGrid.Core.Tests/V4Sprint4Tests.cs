using HabitGrid.Core.Domain;
using Xunit;

namespace HabitGrid.Core.Tests;

public class V4Sprint4Tests
{
    [Fact]
    public void JournalPrompts_RotatesLeadPromptByDay()
    {
        var day1 = new DateOnly(2026, 11, 20);
        var day2 = new DateOnly(2026, 11, 21);

        var p1 = JournalPrompts.GetForDay(day1);
        var p2 = JournalPrompts.GetForDay(day2);

        Assert.Equal(JournalPrompts.All.Count, p1.Count);
        Assert.Equal(JournalPrompts.All.Count, p2.Count);
        Assert.NotEqual(p1[0].Id, p2[0].Id);
    }

    [Fact]
    public void HabitStackRules_RoundTripsJsonAndFiltersInvalid()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var valid = new HabitStackRule(Guid.NewGuid(), a, b, "Sau khi uống nước");
        var selfLoop = new HabitStackRule(Guid.NewGuid(), a, a);

        var json = HabitStackRules.SerializeRules([valid, selfLoop]);
        var parsed = HabitStackRules.ParseRules(json);

        Assert.Single(parsed);
        Assert.Equal(a, parsed[0].TriggerHabitId);
        Assert.Equal(b, parsed[0].NextHabitId);
        Assert.Equal("Sau khi uống nước", parsed[0].CueNote);
    }

    [Fact]
    public void HabitStackRules_FindSuggestionReturnsNextUndoneHabit()
    {
        var a = new Habit { Id = Guid.NewGuid(), Name = "Uống nước" };
        var b = new Habit { Id = Guid.NewGuid(), Name = "Đọc sách" };
        var c = new Habit { Id = Guid.NewGuid(), Name = "Thiền" };

        var habits = new Dictionary<Guid, Habit> { [a.Id] = a, [b.Id] = b, [c.Id] = c };
        var rules = new List<HabitStackRule>
        {
            new(Guid.NewGuid(), a.Id, b.Id),
            new(Guid.NewGuid(), a.Id, c.Id)
        };

        var done1 = new Dictionary<Guid, bool> { [a.Id] = true, [b.Id] = false, [c.Id] = false };
        var s1 = HabitStackRules.FindSuggestion(a.Id, rules, done1, habits);
        Assert.NotNull(s1);
        Assert.Equal(b.Id, s1!.Next.Id);

        // If B is already done, suggest C
        var done2 = new Dictionary<Guid, bool> { [a.Id] = true, [b.Id] = true, [c.Id] = false };
        var s2 = HabitStackRules.FindSuggestion(a.Id, rules, done2, habits);
        Assert.NotNull(s2);
        Assert.Equal(c.Id, s2!.Next.Id);

        // If all targets done, return null
        var done3 = new Dictionary<Guid, bool> { [a.Id] = true, [b.Id] = true, [c.Id] = true };
        Assert.Null(HabitStackRules.FindSuggestion(a.Id, rules, done3, habits));
    }
}
