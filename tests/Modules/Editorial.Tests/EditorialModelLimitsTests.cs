using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests;

/// <summary>
/// The limits on the two endpoints that call the model (#174) are deployment configuration under
/// <c>Editorial:ModelLimits</c>, 10 a minute and 200 a day per editor and endpoint unless it says otherwise: the host
/// reads them once at startup, and a value that is not a whole number from 1 stops it there, naming the key.
/// </summary>
public class EditorialModelLimitsTests
{
    [Fact]
    public void Without_configuration_each_model_endpoint_allows_10_a_minute_and_200_a_day()
    {
        var limits = EditorialModelLimits.From(new ConfigurationBuilder().Build());

        limits.Select(limit => limit.Name).ShouldBe(["editorial.content-review", "editorial.test-draft"]);
        foreach (var limit in limits)
        {
            Windows(limit).ShouldBe([(10, TimeSpan.FromMinutes(1)), (200, TimeSpan.FromDays(1))], limit.Name);
            limit.Refusal.Code.ShouldBe("editorial.model-limit-reached");
        }

        EditorialModelLimits.ContentReview.ShouldBe("editorial.content-review");
        EditorialModelLimits.TestDraft.ShouldBe("editorial.test-draft");
        EditorialModelLimits.DefaultPerMinute.ShouldBe(10);
        EditorialModelLimits.DefaultPerDay.ShouldBe(200);
    }

    [Theory]
    [InlineData("Editorial:ModelLimits:ContentReview:PerMinute", "editorial.content-review", 0)]
    [InlineData("Editorial:ModelLimits:ContentReview:PerDay", "editorial.content-review", 1)]
    [InlineData("Editorial:ModelLimits:TestDraft:PerMinute", "editorial.test-draft", 0)]
    [InlineData("Editorial:ModelLimits:TestDraft:PerDay", "editorial.test-draft", 1)]
    public void Each_configured_value_replaces_its_default(string key, string name, int window)
    {
        var limits = EditorialModelLimits.From(Configured(key, "7"));

        foreach (var limit in limits)
        {
            var expected = limit.Name == name && window == 0 ? 7 : EditorialModelLimits.DefaultPerMinute;
            var expectedDay = limit.Name == name && window == 1 ? 7 : EditorialModelLimits.DefaultPerDay;
            Windows(limit).ShouldBe([(expected, TimeSpan.FromMinutes(1)), (expectedDay, TimeSpan.FromDays(1))], $"{key} for {limit.Name}");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_takes_the_default(string value)
    {
        var limits = EditorialModelLimits.From(Configured(EditorialModelLimits.TestDraftPerDayKey, value));

        foreach (var limit in limits)
        {
            Windows(limit).ShouldBe([(10, TimeSpan.FromMinutes(1)), (200, TimeSpan.FromDays(1))], limit.Name);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("ten")]
    [InlineData("5.0")]
    [InlineData("+5")]
    public void An_unreadable_value_stops_the_host_naming_the_key(string value)
    {
        string[] keys =
        [
            EditorialModelLimits.ContentReviewPerMinuteKey,
            EditorialModelLimits.ContentReviewPerDayKey,
            EditorialModelLimits.TestDraftPerMinuteKey,
            EditorialModelLimits.TestDraftPerDayKey,
        ];
        foreach (var key in keys)
        {
            var read = () => EditorialModelLimits.From(Configured(key, value));

            var refusal = Should.Throw<InvalidOperationException>(read, $"{key} = {value}");
            refusal.Message.ShouldContain(key);
            refusal.Message.ShouldContain("a whole number from 1");
        }
    }

    [Fact]
    public void The_keys_are_the_documented_ones()
    {
        EditorialModelLimits.ContentReviewPerMinuteKey.ShouldBe("Editorial:ModelLimits:ContentReview:PerMinute");
        EditorialModelLimits.ContentReviewPerDayKey.ShouldBe("Editorial:ModelLimits:ContentReview:PerDay");
        EditorialModelLimits.TestDraftPerMinuteKey.ShouldBe("Editorial:ModelLimits:TestDraft:PerMinute");
        EditorialModelLimits.TestDraftPerDayKey.ShouldBe("Editorial:ModelLimits:TestDraft:PerDay");
    }

    private static List<(int Permits, TimeSpan Length)> Windows(UserRateLimit limit) =>
        [.. limit.Windows.Select(window => (window.Permits, window.Length))];

    private static IConfiguration Configured(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
}
