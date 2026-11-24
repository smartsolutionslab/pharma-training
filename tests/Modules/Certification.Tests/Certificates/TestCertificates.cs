using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// Certificates as <c>TestPassedV1Consumer</c> issues them: Anna Berger passed version 3 of "Hygiene 2027" with 85 % at
/// 23:30 UTC on 1 March 2027, already 2 March in Germany, so a yearly course is valid through 2 March 2028.
/// </summary>
internal static class TestCertificates
{
    public const string Code = "k7Qp2xV9mL4rT8wZ1nB6cY3hD5fG0jA2";

    public static readonly DateTimeOffset PassedAt = new(2027, 3, 1, 23, 30, 0, TimeSpan.Zero);

    public static Certificate Issued(PersonId holder) => Issued(holder, "Anna Berger", "Hygiene 2027");

    public static Certificate Issued(PersonId holder, string holderName, string courseTitle) =>
        Issued(holder, holderName, courseTitle, PassedAt, VerificationCode.From(Code));

    /// <summary>A yearly certificate of the holder's in the course, passed at the instant, with a code of its own.</summary>
    public static Certificate Issued(PersonId holder, string courseTitle, DateTimeOffset passedAt) =>
        Issued(holder, "Anna Berger", courseTitle, passedAt, VerificationCodes.Next());

    private static Certificate Issued(PersonId holder, string holderName, string courseTitle, DateTimeOffset passedAt, VerificationCode code)
    {
        var certificate = Certificate.Issue(
            CertificateId.New(),
            AttemptId.From(Guid.CreateVersion7()),
            holder,
            HolderName.From(holderName),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            CourseTitle.From(courseTitle),
            CourseVersionNumber.From(3),
            RecurrenceInterval.Yearly,
            ScorePercent.From(85),
            code,
            Timestamp.From(passedAt));
        certificate.ClearDomainEvents();
        return certificate;
    }
}
