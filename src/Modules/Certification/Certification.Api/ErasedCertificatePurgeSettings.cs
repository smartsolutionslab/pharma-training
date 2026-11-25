using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;

/// <summary>
/// How often Certification purges the retained certificates of erased persons that no pharmacy keeps any longer (#115,
/// slice 5), read once at startup from <c>Certification:ErasedCertificatePurge:Interval</c>. Not implemented yet.
/// </summary>
public sealed record ErasedCertificatePurgeSettings
{
    public const string IntervalKey = "Certification:ErasedCertificatePurge:Interval";

    private ErasedCertificatePurgeSettings(TimeSpan interval) => Interval = interval;

    /// <summary>The time between two passes.</summary>
    public TimeSpan Interval { get; }

    public static ErasedCertificatePurgeSettings From(IConfiguration configuration) =>
        throw new NotImplementedException($"Reading {IntervalKey} is not implemented yet ({configuration}).");
}
