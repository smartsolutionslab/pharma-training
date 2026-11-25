using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>A retention period is missing or lies outside two to thirty whole years (#391).</summary>
public sealed record InvalidRetentionPeriod()
    : ApiError(
        "certification.invalid-retention-period",
        $"A retention period is a whole number of years from {RetentionPeriod.MinimumYears} to {RetentionPeriod.MaximumYears}.",
        HttpStatusCode.BadRequest);
