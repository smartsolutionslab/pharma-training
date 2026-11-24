using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// The announcement sweep of a module (#277) reads its settings once at startup from the module's own section,
/// <c>&lt;Module&gt;:AnnouncementSweep</c>: a blank or absent value takes the default, and a value that does not parse
/// or lies outside its range stops the host, naming the key and the value.
/// </summary>
public class AnnouncementSweepSettingsTests
{
    private const string Section = "Learning:AnnouncementSweep";

    [Fact]
    public void Absent_or_blank_settings_take_the_defaults()
    {
        var absent = AnnouncementSweepSettings.From(Settings(), "Learning");
        var blank = AnnouncementSweepSettings.From(
            Settings(($"{Section}:Interval", string.Empty), ($"{Section}:GracePeriod", " "), ($"{Section}:Lookback", "  "), ($"{Section}:BatchSize", string.Empty)),
            "Learning");

        foreach (var settings in new[] { absent, blank })
        {
            settings.Interval.ShouldBe(TimeSpan.FromMinutes(1));
            settings.GracePeriod.ShouldBe(TimeSpan.FromMinutes(2));
            settings.Lookback.ShouldBe(TimeSpan.FromDays(7));
            settings.BatchSize.ShouldBe(100);
        }
    }

    [Theory]
    [InlineData("Interval", "00:00:01")]
    [InlineData("Interval", "01:00:00")]
    [InlineData("GracePeriod", "00:00:01")]
    [InlineData("GracePeriod", "01:00:00")]
    [InlineData("Lookback", "01:00:00")]
    [InlineData("Lookback", "90.00:00:00")]
    [InlineData("BatchSize", "1")]
    [InlineData("BatchSize", "1000")]
    public void Settings_within_their_ranges_are_read(string key, string value)
    {
        var settings = AnnouncementSweepSettings.From(Settings(($"{Section}:{key}", value)), "Learning");

        ValueOf(settings, key).ShouldBe(Expected(key, value));
    }

    [Theory]
    [InlineData("Interval", "0")]
    [InlineData("Interval", "00:00:00")]
    [InlineData("Interval", "01:00:01")]
    [InlineData("Interval", "-00:00:05")]
    [InlineData("Interval", "abc")]
    [InlineData("GracePeriod", "00:00:00")]
    [InlineData("GracePeriod", "01:00:01")]
    [InlineData("GracePeriod", "abc")]
    [InlineData("Lookback", "00:59:59")]
    [InlineData("Lookback", "90.00:00:01")]
    [InlineData("Lookback", "abc")]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "1001")]
    [InlineData("BatchSize", "-1")]
    [InlineData("BatchSize", "1.5")]
    [InlineData("BatchSize", "abc")]
    public void A_setting_outside_its_range_or_unreadable_stops_the_host_naming_the_key(string key, string value)
    {
        var refused = Should.Throw<InvalidOperationException>(() => AnnouncementSweepSettings.From(Settings(($"{Section}:{key}", value)), "Learning"));

        refused.Message.ShouldContain($"{Section}:{key}", Case.Sensitive);
        refused.Message.ShouldContain($"'{value}'", Case.Sensitive);
    }

    /// <summary>The lookback's least (one hour) is the grace period's most, so the two meet only there.</summary>
    [Fact]
    public void A_lookback_not_longer_than_the_grace_period_is_refused()
    {
        var refused = Should.Throw<InvalidOperationException>(() =>
            AnnouncementSweepSettings.From(Settings(($"{Section}:Lookback", "01:00:00"), ($"{Section}:GracePeriod", "01:00:00")), "Learning"));

        refused.Message.ShouldContain($"{Section}:Lookback", Case.Sensitive);
        refused.Message.ShouldContain($"{Section}:GracePeriod", Case.Sensitive);
    }

    [Fact]
    public void The_section_is_the_modules_own()
    {
        var configuration = Settings(
            ("Assessment:AnnouncementSweep:Interval", "00:00:05"),
            ("Assessment:AnnouncementSweep:GracePeriod", "00:00:07"),
            ("Assessment:AnnouncementSweep:Lookback", "02:00:00"),
            ("Assessment:AnnouncementSweep:BatchSize", "abc"));

        var learning = AnnouncementSweepSettings.From(configuration, "Learning");

        learning.Interval.ShouldBe(TimeSpan.FromMinutes(1));
        learning.GracePeriod.ShouldBe(TimeSpan.FromMinutes(2));
        learning.Lookback.ShouldBe(TimeSpan.FromDays(7));
        learning.BatchSize.ShouldBe(100);
        Should.Throw<InvalidOperationException>(() => AnnouncementSweepSettings.From(configuration, "Assessment"))
            .Message.ShouldContain("Assessment:AnnouncementSweep:BatchSize", Case.Sensitive);
    }

    private static object ValueOf(AnnouncementSweepSettings settings, string key) =>
        key switch
        {
            "Interval" => settings.Interval,
            "GracePeriod" => settings.GracePeriod,
            "Lookback" => settings.Lookback,
            "BatchSize" => settings.BatchSize,
            _ => throw new InvalidOperationException($"The test names no setting {key}."),
        };

    private static object Expected(string key, string value) =>
        key == "BatchSize"
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : TimeSpan.Parse(value, CultureInfo.InvariantCulture);

    private static IConfiguration Settings(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
