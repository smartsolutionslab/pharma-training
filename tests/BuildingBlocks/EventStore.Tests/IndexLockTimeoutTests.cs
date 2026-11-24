using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore.Tests;

/// <summary>
/// The MigrationRunner bounds each lock wait of a swept event index's concurrent build and drop (#351) by the runner's
/// setting <c>Migrations:IndexLockTimeout</c> (#359): 30 s when it is blank or absent; a value that does not parse or lies
/// outside one second to one hour fails the run, naming the key and the value.
/// </summary>
public class IndexLockTimeoutTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_absent_or_blank_setting_takes_thirty_seconds(string? value)
    {
        var lockTimeout = IndexLockTimeout.From(Settings(value));

        lockTimeout.Value.ShouldBe(TimeSpan.FromSeconds(30));
        lockTimeout.Statement.ShouldBe("set lock_timeout = '30000ms'");
    }

    [Theory]
    [InlineData("00:00:01", "set lock_timeout = '1000ms'")]
    [InlineData("00:00:02.5", "set lock_timeout = '2500ms'")]
    [InlineData("00:05:00", "set lock_timeout = '300000ms'")]
    [InlineData("01:00:00", "set lock_timeout = '3600000ms'")]
    public void A_setting_within_its_range_bounds_the_lock_waits_by_it(string value, string statement)
    {
        var lockTimeout = IndexLockTimeout.From(Settings(value));

        lockTimeout.Value.ShouldBe(TimeSpan.Parse(value, CultureInfo.InvariantCulture));
        lockTimeout.Statement.ShouldBe(statement);
    }

    /// <summary>
    /// PostgreSQL reads a <c>lock_timeout</c> of zero as no bound at all, so zero, and anything that would round to it, is
    /// refused rather than passed on; an hour is longer than any rollout waits for an init container.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.999")]
    [InlineData("-00:00:05")]
    [InlineData("01:00:01")]
    [InlineData("1.00:00:00")]
    [InlineData("30")]
    [InlineData("30s")]
    [InlineData("abc")]
    public void A_setting_outside_its_range_or_unreadable_fails_the_run_naming_the_key_and_the_value(string value)
    {
        var refused = Should.Throw<InvalidOperationException>(() => IndexLockTimeout.From(Settings(value)));

        refused.Message.ShouldContain("Migrations:IndexLockTimeout", Case.Sensitive);
        refused.Message.ShouldContain($"'{value}'", Case.Sensitive);
    }

    private static IConfiguration Settings(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(IndexLockTimeout.Setting, value)])
            .Build();
}
