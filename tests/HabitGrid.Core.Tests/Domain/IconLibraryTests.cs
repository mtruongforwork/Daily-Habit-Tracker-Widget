using FluentAssertions;
using HabitGrid.Core.Domain.Icons;
using Xunit;

namespace HabitGrid.Core.Tests.Domain;

public class IconLibraryTests
{
    [Fact]
    public void Library_HasNoDuplicateEmoji_AndEveryCategoryIsUsed()
    {
        IconLibrary.All.Select(i => i.Emoji).Should().OnlyHaveUniqueItems();
        Enum.GetValues<IconCategory>().Should().OnlyContain(c => IconLibrary.InCategory(c).Count >= 5);
        IconLibrary.All.Count.Should().BeGreaterThan(100);
    }

    [Fact]
    public void Keywords_AreFoldedLowercase()
    {
        IconLibrary.All.Should().OnlyContain(i => i.Keywords == IconLibrary.Fold(i.Keywords) && i.Keywords.Length > 0);
    }

    [Theory]
    [InlineData("Chạy bộ", "🏃")]
    [InlineData("CHAY", "🏃")]
    [InlineData("đọc sách", "📚")]
    [InlineData("water", "💧")]
    [InlineData("yoga", "🧘")]
    public void Search_IgnoresCaseAndDiacritics(string query, string expected)
    {
        IconLibrary.Search(query).Select(i => i.Emoji).Should().Contain(expected);
    }

    [Fact]
    public void Search_RequiresEveryWord_AndEmptyReturnsAll()
    {
        IconLibrary.Search("chay bo").Select(i => i.Emoji).Should().Contain("🏃").And.NotContain("📚");
        IconLibrary.Search("  ").Should().HaveCount(IconLibrary.All.Count);
        IconLibrary.Search("zzzzqq").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Chạy bộ buổi sáng", "🏃")]
    [InlineData("Uống nước", "💧")]
    [InlineData("Đọc sách mỗi ngày", "📚")]
    [InlineData("Tập gym", "🏋️")]
    [InlineData("Học tiếng Anh", "🌐")]
    public void Suggest_PutsTheBestMatchFirstOrNearTheTop(string name, string expected)
    {
        IconLibrary.Suggest(name).Select(i => i.Emoji).Should().Contain(expected);
    }

    [Fact]
    public void Suggest_IsEmptyForBlankOrUnknownNames_AndCapped()
    {
        IconLibrary.Suggest(null).Should().BeEmpty();
        IconLibrary.Suggest("   ").Should().BeEmpty();
        IconLibrary.Suggest("qqqq zzzz").Should().BeEmpty();
        IconLibrary.Suggest("an uong tap luyen nuoc sach", 3).Count.Should().BeLessThanOrEqualTo(3);
    }
}
