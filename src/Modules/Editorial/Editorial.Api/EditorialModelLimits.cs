using System.Globalization;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

/// <summary>
/// The per-editor limits on the two endpoints that call the model (docs/adr/0020), each with a budget of its own:
/// <see cref="DefaultPerMinute"/> a minute and <see cref="DefaultPerDay"/> a day unless the host's configuration says
/// otherwise under <c>Editorial:ModelLimits</c> (ADR 0007). A blank or absent value takes the default; a value that is
/// not a whole number from 1 stops the host at startup, naming the key.
/// </summary>
public static class EditorialModelLimits
{
    public const string ContentReview = "editorial.content-review";

    public const string TestDraft = "editorial.test-draft";

    public const int DefaultPerMinute = 10;

    public const int DefaultPerDay = 200;

    public const string ContentReviewPerMinuteKey = "Editorial:ModelLimits:ContentReview:PerMinute";

    public const string ContentReviewPerDayKey = "Editorial:ModelLimits:ContentReview:PerDay";

    public const string TestDraftPerMinuteKey = "Editorial:ModelLimits:TestDraft:PerMinute";

    public const string TestDraftPerDayKey = "Editorial:ModelLimits:TestDraft:PerDay";

    /// <summary>The content review's limit, then the test draft's.</summary>
    public static IReadOnlyList<UserRateLimit> From(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        return
        [
            Limit(ContentReview, PermitsAt(configuration, ContentReviewPerMinuteKey, DefaultPerMinute), PermitsAt(configuration, ContentReviewPerDayKey, DefaultPerDay)),
            Limit(TestDraft, PermitsAt(configuration, TestDraftPerMinuteKey, DefaultPerMinute), PermitsAt(configuration, TestDraftPerDayKey, DefaultPerDay)),
        ];
    }

    private static UserRateLimit Limit(string name, int perMinute, int perDay) =>
        UserRateLimit.Of(
            name,
            ModelLimitFailures.Reached(),
            UserRateWindow.Of(perMinute, TimeSpan.FromMinutes(1)),
            UserRateWindow.Of(perDay, TimeSpan.FromDays(1)));

    private static int PermitsAt(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var permits) || permits < 1)
        {
            throw new InvalidOperationException($"The setting {key} is '{value}'; it accepts a whole number from 1, or nothing for {fallback}.");
        }

        return permits;
    }
}
