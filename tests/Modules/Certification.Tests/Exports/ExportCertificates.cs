using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// Certificates as an export reads them: version 3 of a yearly course, passed with 85 % by the holder at the instant
/// given, with their PDF still pending or stored at <c>certificates/{id}.pdf</c> as the renderer stores it.
/// </summary>
internal static class ExportCertificates
{
    public static Certificate Pending(PersonId holder, DateTimeOffset issuedAt, string holderName = "Anna Berger", string courseTitle = "Hygiene 2027")
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
            VerificationCodes.Next(),
            Timestamp.From(issuedAt));
        certificate.ClearDomainEvents();
        return certificate;
    }

    public static Certificate Stored(PersonId holder, DateTimeOffset issuedAt, string holderName = "Anna Berger", string courseTitle = "Hygiene 2027")
    {
        var certificate = Pending(holder, issuedAt, holderName, courseTitle);
        certificate.AttachDocument(DocumentReference.From(LocationOf(certificate)));
        return certificate;
    }

    public static string LocationOf(Certificate certificate) => $"certificates/{certificate.Id.Value:D}.pdf";
}
