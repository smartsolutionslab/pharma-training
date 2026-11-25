using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// A module's sweep of erased persons' messages (#439) reads its interval once at startup from the module's own section,
/// <c>&lt;Module&gt;:ErasedPersonMessageSweep:Interval</c>, in the invariant <c>c</c> format: a blank or absent value takes
/// one minute, and a value that does not parse or lies outside one second to one hour stops the host, naming the key,
/// the value and what the setting accepts.
/// </summary>
public class ErasedPersonMessageSweepSettingsTests
{
    private const string Key = "Learning:ErasedPersonMessageSweep:Interval";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void An_absent_or_blank_interval_takes_one_minute(string? value)
    {
        var settings = ErasedPersonMessageSweepSettings.From(Settings((Key, value)), "Learning");

        settings.Interval.ShouldBe(TimeSpan.FromMinutes(1));
        ErasedPersonMessageSweepSettings.DefaultInterval.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:05:00")]
    [InlineData("01:00:00")]
    public void An_interval_inside_its_range_is_read(string value)
    {
        var settings = ErasedPersonMessageSweepSettings.From(Settings((Key, value)), "Learning");

        settings.Interval.ShouldBe(TimeSpan.Parse(value, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("00:00:00.5")]
    [InlineData("01:00:01")]
    [InlineData("-00:01:00")]
    public void An_interval_that_does_not_parse_or_lies_outside_its_range_stops_the_host_naming_the_key_and_the_value(string value)
    {
        var refused = Should.Throw<InvalidOperationException>(() => ErasedPersonMessageSweepSettings.From(Settings((Key, value)), "Learning"));

        refused.Message.ShouldBe($"The setting {Key} is '{value}'; it accepts a duration from 00:00:01 to 01:00:00, or nothing for 00:01:00.");
    }

    [Fact]
    public void Each_module_reads_its_own_section()
    {
        var configuration = Settings(
            ("Compliance:ErasedPersonMessageSweep:Interval", "00:00:05"),
            ("Certification:ErasedPersonMessageSweep:Interval", "soon"),
            ("Learning:AnnouncementSweep:Interval", "00:00:09"));

        ErasedPersonMessageSweepSettings.From(configuration, "Learning").Interval.ShouldBe(TimeSpan.FromMinutes(1));
        ErasedPersonMessageSweepSettings.From(configuration, "Compliance").Interval.ShouldBe(TimeSpan.FromSeconds(5));
        Should.Throw<InvalidOperationException>(() => ErasedPersonMessageSweepSettings.From(configuration, "Certification"))
            .Message.ShouldContain("Certification:ErasedPersonMessageSweep:Interval", Case.Sensitive);
    }

    private static IConfiguration Settings(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
