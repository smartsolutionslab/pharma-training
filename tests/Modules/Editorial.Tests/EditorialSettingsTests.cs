using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests;

/// <summary>
/// Four eyes is deployment configuration, <c>Editorial:FourEyes</c>, off unless it says otherwise (ADR 0007): the host
/// reads it once at startup, and a value it cannot read stops the host there rather than on a publish. The bounds on
/// reading a PDF's text (#316) are <c>Editorial:PdfText:MaxConcurrentParses</c> (1 to 16, 2 when absent or blank) and
/// <c>Editorial:PdfText:TimeoutSeconds</c> (1 to 300, 30 when absent or blank), read the same way.
/// </summary>
public class EditorialSettingsTests
{
    private const string MaxConcurrentParsesKey = "Editorial:PdfText:MaxConcurrentParses";

    private const string TimeoutSecondsKey = "Editorial:PdfText:TimeoutSeconds";

    [Fact]
    public void Four_eyes_is_off_when_nothing_is_configured()
    {
        var configuration = new ConfigurationBuilder().Build();

        var review = EditorialSettings.ReviewFrom(configuration);

        review.Policy.ShouldBe(ReviewPolicy.SingleEditor);
    }

    [Theory]
    [InlineData("true", ReviewPolicy.FourEyes)]
    [InlineData("True", ReviewPolicy.FourEyes)]
    [InlineData("false", ReviewPolicy.SingleEditor)]
    [InlineData(" ", ReviewPolicy.SingleEditor)]
    public void Four_eyes_follows_the_configured_value(string value, ReviewPolicy expected)
    {
        var review = EditorialSettings.ReviewFrom(Configured(value));

        review.Policy.ShouldBe(expected);
    }

    [Fact]
    public void An_unreadable_value_stops_the_host_naming_the_key()
    {
        var read = () => EditorialSettings.ReviewFrom(Configured("yes"));

        Should.Throw<InvalidOperationException>(read).Message.ShouldContain("Editorial:FourEyes");
    }

    [Fact]
    public void The_pdf_readers_are_two_with_a_30_second_deadline_when_nothing_is_configured()
    {
        var limits = EditorialSettings.PdfTextFrom(new ConfigurationBuilder().Build());

        limits.MaxConcurrentParses.ShouldBe(2);
        limits.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_pdf_reader_setting_takes_its_default(string value)
    {
        var limits = EditorialSettings.PdfTextFrom(Configured(new Dictionary<string, string?> { [MaxConcurrentParsesKey] = value, [TimeoutSecondsKey] = value }));

        limits.MaxConcurrentParses.ShouldBe(2);
        limits.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("16", 16)]
    public void The_pdf_reader_limit_takes_a_whole_number_from_1_to_16(string value, int expected)
    {
        var limits = EditorialSettings.PdfTextFrom(Configured(new Dictionary<string, string?> { [MaxConcurrentParsesKey] = value }));

        limits.MaxConcurrentParses.ShouldBe(expected);
        limits.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("300", 300)]
    public void The_pdf_read_deadline_takes_a_whole_number_of_seconds_from_1_to_300(string value, int expectedSeconds)
    {
        var limits = EditorialSettings.PdfTextFrom(Configured(new Dictionary<string, string?> { [TimeoutSecondsKey] = value }));

        limits.Timeout.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
        limits.MaxConcurrentParses.ShouldBe(2);
    }

    [Theory]
    [InlineData(MaxConcurrentParsesKey, "0")]
    [InlineData(MaxConcurrentParsesKey, "17")]
    [InlineData(MaxConcurrentParsesKey, "-1")]
    [InlineData(MaxConcurrentParsesKey, "2.5")]
    [InlineData(MaxConcurrentParsesKey, "two")]
    [InlineData(MaxConcurrentParsesKey, " 3")]
    [InlineData(TimeoutSecondsKey, "0")]
    [InlineData(TimeoutSecondsKey, "301")]
    public void A_pdf_reader_setting_out_of_its_range_stops_the_host_naming_the_key_and_the_value(string key, string value)
    {
        var read = () => EditorialSettings.PdfTextFrom(Configured(new Dictionary<string, string?> { [key] = value }));

        var refused = Should.Throw<InvalidOperationException>(read);
        refused.Message.ShouldContain(key);
        refused.Message.ShouldContain($"'{value}'");
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(17, 30)]
    [InlineData(2, 0)]
    [InlineData(2, 301)]
    public void Pdf_text_limits_out_of_their_range_are_refused(int maxConcurrentParses, int timeoutSeconds)
    {
        var of = () => PdfTextLimits.Of(maxConcurrentParses, TimeSpan.FromSeconds(timeoutSeconds));

        Should.Throw<GuardException>(of);
    }

    [Fact]
    public void Pdf_text_limits_at_the_ends_of_their_range_are_kept()
    {
        var lowest = PdfTextLimits.Of(1, TimeSpan.FromSeconds(1));
        var highest = PdfTextLimits.Of(16, TimeSpan.FromSeconds(300));

        lowest.MaxConcurrentParses.ShouldBe(1);
        lowest.Timeout.ShouldBe(TimeSpan.FromSeconds(1));
        highest.MaxConcurrentParses.ShouldBe(16);
        highest.Timeout.ShouldBe(TimeSpan.FromSeconds(300));
    }

    private static IConfiguration Configured(string value) =>
        Configured(new Dictionary<string, string?> { ["Editorial:FourEyes"] = value });

    private static IConfiguration Configured(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
