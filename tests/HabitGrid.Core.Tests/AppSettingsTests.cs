using FluentAssertions;
using HabitGrid.Core.Settings;
using Xunit;

namespace HabitGrid.Core.Tests;

/// <summary>
/// Bộ kiểm thử cài đặt: tự lưu ngay, giá trị mặc định và kẹp trong khoảng cho phép.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void Defaults_ShouldMatchSpecification()
    {
        var settings = new AppSettings(new InMemorySettingsRepository());

        settings.Theme.Should().Be(AppThemeMode.System);
        settings.Language.Should().Be(AppLanguage.Vietnamese);
        settings.WeekStart.Should().Be(DayOfWeek.Monday);
        settings.DayStartHour.Should().Be(0);
        settings.RunAtStartup.Should().BeFalse();
        settings.PinningMode.Should().Be(0);
        settings.DefaultWidgetSize.Should().Be(1);
        settings.EditMode.Should().BeFalse();
        settings.BackgroundStyle.Should().Be(WidgetBackgroundStyle.Acrylic);
        settings.BackgroundOpacity.Should().Be(85);
        settings.CornerRadius.Should().Be(12);
    }

    [Fact]
    public async Task Setting_ShouldBePersistedImmediately()
    {
        var repository = new InMemorySettingsRepository();
        var settings = new AppSettings(repository);

        settings.Theme = AppThemeMode.Dark;
        settings.DayStartHour = 3;
        settings.CornerRadius = 16;

        await settings.FlushAsync();

        // Nạp lại từ CSDL -> giá trị phải được giữ nguyên.
        var reloaded = await AppSettings.CreateAsync(repository);

        reloaded.Theme.Should().Be(AppThemeMode.Dark);
        reloaded.DayStartHour.Should().Be(3);
        reloaded.CornerRadius.Should().Be(16);
    }

    [Fact]
    public void SettingChanged_ShouldRaiseWithKey()
    {
        var settings = new AppSettings(new InMemorySettingsRepository());
        string? changedKey = null;

        settings.SettingChanged += (_, key) => changedKey = key;

        settings.Theme = AppThemeMode.Light;

        changedKey.Should().Be(SettingsKeys.Theme);
    }

    [Fact]
    public void SettingChanged_ShouldNotRaiseWhenValueUnchanged()
    {
        var settings = new AppSettings(new InMemorySettingsRepository());
        var count = 0;

        settings.SettingChanged += (_, _) => count++;

        settings.Theme = AppThemeMode.System;
        settings.Theme = AppThemeMode.System;

        count.Should().Be(0);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(-3, 0)]
    [InlineData(3, 3)]
    public void DayStartHour_ShouldBeClampedToValidRange(int input, int expected)
    {
        var settings = new AppSettings(new InMemorySettingsRepository());

        settings.DayStartHour = input;

        settings.DayStartHour.Should().Be(expected);
    }

    [Theory]
    [InlineData(200, 100)]
    [InlineData(-10, 0)]
    [InlineData(50, 50)]
    public void BackgroundOpacity_ShouldBeClamped(double input, double expected)
    {
        var settings = new AppSettings(new InMemorySettingsRepository());

        settings.BackgroundOpacity = input;

        settings.BackgroundOpacity.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(40, 16)]
    [InlineData(10, 10)]
    public void CornerRadius_ShouldBeClamped(double input, double expected)
    {
        var settings = new AppSettings(new InMemorySettingsRepository());

        settings.CornerRadius = input;

        settings.CornerRadius.Should().Be(expected);
    }

    [Fact]
    public void Language_ShouldMapToCulture()
    {
        AppLanguage.Vietnamese.ToCultureInfo().Name.Should().Be("vi-VN");
        AppLanguage.English.ToCultureInfo().Name.Should().Be("en-US");
        AppLanguage.Japanese.ToCultureInfo().Name.Should().Be("ja-JP");
        AppLanguage.Korean.ToCultureInfo().Name.Should().Be("ko-KR");
        AppLanguage.Vietnamese.ToCultureName().Should().Be("vi-VN");
    }
}
