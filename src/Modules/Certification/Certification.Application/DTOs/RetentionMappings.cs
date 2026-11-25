using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

public static class RetentionMappings
{
    /// <summary>The period the pharmacy chose, or the default of five years when it chose none (#391).</summary>
    public static CertificationRetentionPeriodDto ToCertificationRetentionPeriodDto(this Option<PharmacyRetention> stored) =>
        stored.Match(
            retention => new CertificationRetentionPeriodDto(retention.Period.Value, false),
            () => new CertificationRetentionPeriodDto(RetentionPeriod.DefaultYears, true));
}
