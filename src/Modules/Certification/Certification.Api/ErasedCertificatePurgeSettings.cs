using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;

/// <summary>
/// How often Certification purges the retained certificates of erased persons that no pharmacy keeps any longer (#115,
/// slice 5), read once at startup from <c>Certification:ErasedCertificatePurge:Interval</c>. A blank or absent value
/// takes the default, once a day; a value that does not parse in the invariant <c>c</c> format (<c>[d.]hh:mm:ss</c>) or
/// lies outside one second to seven days stops the host, naming the key, the value and what it accepts.
/// </summary>
public sealed record ErasedCertificatePurgeSettings
{
    public const string IntervalKey = "Certification:ErasedCertificatePurge:Interval";

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromDays(1);

    private static readonly TimeSpan LeastInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MostInterval = TimeSpan.FromDays(7);

    private ErasedCertificatePurgeSettings(TimeSpan interval) => Interval = interval;

    /// <summary>The time between two passes.</summary>
    public TimeSpan Interval { get; }

    public static ErasedCertificatePurgeSettings From(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var value = configuration[IntervalKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ErasedCertificatePurgeSettings(DefaultInterval);
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var interval) || interval < LeastInterval || interval > MostInterval)
        {
            throw new InvalidOperationException(
                $"The setting {IntervalKey} is '{value}'; it accepts a duration from {Render(LeastInterval)} to {Render(MostInterval)}, or nothing for {Render(DefaultInterval)}.");
        }

        return new ErasedCertificatePurgeSettings(interval);
    }

    private static string Render(TimeSpan duration) => duration.ToString("c", CultureInfo.InvariantCulture);
}
