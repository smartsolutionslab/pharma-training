using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>Certification's CertificateIssuedV1 as it arrives: published by its unit of work, so it carries its metadata.</summary>
internal static class CertificateEvents
{
    public static CertificateIssuedV1 Issued(Guid personId, Guid courseId, DateTimeOffset issuedAt) =>
        Issued(Guid.CreateVersion7(), personId, courseId, issuedAt);

    public static CertificateIssuedV1 Issued(Guid certificateId, Guid personId, Guid courseId, DateTimeOffset issuedAt) =>
        new(certificateId, personId, courseId, Guid.CreateVersion7(), issuedAt, DateOnly.FromDateTime(issuedAt.UtcDateTime).AddMonths(24))
        {
            Metadata = new EventMetadata(Guid.CreateVersion7(), issuedAt),
        };
}
