using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>A yearly certificate for Anna Berger in Hygiene 2027, passed at 23:30 UTC on 1 March 2027 (2 March in Germany).</summary>
internal static class IssuedCertificates
{
    public static readonly DateTimeOffset PassedAt = new(2027, 3, 1, 23, 30, 0, TimeSpan.Zero);

    public static Certificate Yearly() =>
        Certificate.Issue(
            CertificateId.New(),
            AttemptId.From(Guid.CreateVersion7()),
            PersonId.From(Guid.CreateVersion7()),
            HolderName.From("Anna Berger"),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            CourseTitle.From("Hygiene 2027"),
            RecurrenceInterval.Yearly,
            ScorePercent.From(90),
            VerificationCodes.Next(),
            Timestamp.From(PassedAt));
}
