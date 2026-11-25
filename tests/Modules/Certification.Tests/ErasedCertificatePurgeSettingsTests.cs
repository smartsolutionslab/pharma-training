using Microsoft.Extensions.Configuration;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests;

/// <summary>
/// How often Certification purges erased persons' certificates (#115, slice 5) is deployment configuration under
/// <c>Certification:ErasedCertificatePurge:Interval</c>, once a day unless it says otherwise: the host reads it once at
/// startup in the invariant <c>c</c> format, and a value that does not parse or lies outside one second to seven days
/// stops it there, naming the key and the value.
/// </summary>
public class ErasedCertificatePurgeSettingsTests
{
    private const string IntervalKey = "Certification:ErasedCertificatePurge:Interval";

    /// <summary>
    /// Hourly, not daily: the first pass runs one interval after the host starts, so a service restarted more often
    /// than once a day would never purge with a daily default.
    /// </summary>
    [Fact]
    public void Without_configuration_the_purge_runs_once_an_hour()
    {
        var settings = ErasedCertificatePurgeSettings.From(new ConfigurationBuilder().Build());

        settings.Interval.ShouldBe(TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_takes_the_default(string value)
    {
        var settings = ErasedCertificatePurgeSettings.From(Configured(value));

        settings.Interval.ShouldBe(TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData("00:00:01", 0, 0, 1)]
    [InlineData("06:00:00", 0, 6, 0)]
    [InlineData("7.00:00:00", 7, 0, 0)]
    public void A_configured_interval_within_one_second_and_seven_days_is_read(string value, int days, int hours, int seconds)
    {
        var settings = ErasedCertificatePurgeSettings.From(Configured(value));

        settings.Interval.ShouldBe(new TimeSpan(days, hours, 0, seconds));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.5")]
    [InlineData("-00:00:05")]
    [InlineData("7.00:00:01")]
    [InlineData("abc")]
    public void An_interval_outside_its_range_or_unreadable_stops_the_host_naming_the_key(string value)
    {
        var refused = Should.Throw<InvalidOperationException>(() => ErasedCertificatePurgeSettings.From(Configured(value)));

        refused.Message.ShouldContain(IntervalKey, Case.Sensitive);
        refused.Message.ShouldContain($"'{value}'", Case.Sensitive);
    }

    private static IConfiguration Configured(string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [IntervalKey] = value })
            .Build();
}
