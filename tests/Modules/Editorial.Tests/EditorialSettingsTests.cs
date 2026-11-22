using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests;

/// <summary>
/// Four eyes is deployment configuration, <c>Editorial:FourEyes</c>, off unless it says otherwise (ADR 0005): the host
/// reads it once at startup, and a value it cannot read stops the host there rather than on a publish.
/// </summary>
public class EditorialSettingsTests
{
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

    private static IConfiguration Configured(string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Editorial:FourEyes"] = value })
            .Build();
}
