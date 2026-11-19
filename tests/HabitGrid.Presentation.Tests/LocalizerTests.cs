using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.Templates;

namespace HabitGrid.Presentation.Tests;

public sealed class LocalizerTests
{
    [Theory]
    [InlineData("vi", "vi", "Hôm nay")]
    [InlineData("en", "en", "Today")]
    [InlineData("ja", "ja", "今日")]
    [InlineData("ko", "ko", "오늘")]
    [InlineData("ja-JP", "ja", "今日")]
    [InlineData("ko-KR", "ko", "오늘")]
    [InlineData("fr", "vi", "Hôm nay")]
    public void SetLanguage_ResolvesExpectedCultureAndStrings(string input, string expectedCode, string expectedToday)
    {
        var loc = new Localizer();
        loc.SetLanguage(input);

        loc.Language.Should().Be(expectedCode);
        loc["nav.today"].Should().Be(expectedToday);
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged_OnlyWhenChanged()
    {
        var loc = new Localizer();
        var events = 0;
        loc.LanguageChanged += (_, _) => events++;

        loc.SetLanguage("vi");
        events.Should().Be(0);

        loc.SetLanguage("ja");
        loc.SetLanguage("ja-JP");
        loc.SetLanguage("ko");
        events.Should().Be(2);
    }

    [Fact]
    public void AllFourResxFiles_HaveIdenticalKeys_NonEmptyValues_AndMatchingPlaceholders()
    {
        var root = FindRepoRoot();
        var dir = Path.Combine(root, "src", "HabitGrid.Presentation", "Resources");
        var files = new[]
        {
            ("vi", Path.Combine(dir, "Strings.resx")),
            ("en", Path.Combine(dir, "Strings.en.resx")),
            ("ja", Path.Combine(dir, "Strings.ja.resx")),
            ("ko", Path.Combine(dir, "Strings.ko.resx"))
        };

        var maps = files.ToDictionary(f => f.Item1, f => ReadResx(f.Item2));
        var baseline = maps["vi"];
        baseline.Should().NotBeEmpty();

        foreach (var (lang, map) in maps)
        {
            map.Keys.Should().BeEquivalentTo(baseline.Keys, $"file {lang} must have the exact same keys as vi");

            foreach (var (key, val) in map)
            {
                val.Should().NotBeNullOrWhiteSpace($"key '{key}' in {lang} must not be empty");
                Placeholders(val).Should().BeEquivalentTo(Placeholders(baseline[key]), $"placeholders for '{key}' in {lang} must match vi");
            }
        }
    }

    [Fact]
    public void HabitTemplate_NonVietnamese_FallsBackToEnglish()
    {
        var template = HabitTemplates.All[0];
        template.Name("vi").Should().Be(template.NameVi);
        template.Name("en").Should().Be(template.NameEn);
        template.Name("ja").Should().Be(template.NameEn);
        template.Name("ko").Should().Be(template.NameEn);
    }

    private static Dictionary<string, string> ReadResx(string path)
    {
        File.Exists(path).Should().BeTrue($"missing resx file: {path}");
        var doc = XDocument.Load(path);
        return doc.Root!
            .Elements("data")
            .ToDictionary(
                e => (string)e.Attribute("name")!,
                e => (string?)e.Element("value") ?? string.Empty);
    }

    private static IReadOnlyList<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{[0-9a-zA-Z_:]+\}").Select(m => m.Value).OrderBy(v => v).ToList();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HabitGrid.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("HabitGrid.sln not found.");
    }
}
