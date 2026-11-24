using System.Globalization;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;

/// <summary>
/// The per-address limit on public verification (#51, docs/adr/0026): <see cref="DefaultPerMinute"/> a minute and
/// <see cref="DefaultPerDay"/> a day per client address unless the host's configuration says otherwise under
/// <c>Certification:VerificationLimits</c>. A blank or absent value takes the default; a value that is not a whole
/// number from 1 stops the host at startup, naming the key.
/// </summary>
public static class CertificationVerificationLimits
{
    public const string Verify = "certification.verify";

    public const int DefaultPerMinute = 30;

    public const int DefaultPerDay = 500;

    public const string PerMinuteKey = "Certification:VerificationLimits:PerMinute";

    public const string PerDayKey = "Certification:VerificationLimits:PerDay";

    public static UserRateLimit From(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        return UserRateLimit.Of(
            Verify,
            VerificationFailures.LimitReached(),
            UserRateWindow.Of(PermitsAt(configuration, PerMinuteKey, DefaultPerMinute), TimeSpan.FromMinutes(1)),
            UserRateWindow.Of(PermitsAt(configuration, PerDayKey, DefaultPerDay), TimeSpan.FromDays(1)));
    }

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
