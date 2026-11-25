using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>
/// Proof that a person passed one course version. Issued once per passed attempt (a unique index on the attempt makes
/// a redelivered TestPassed harmless), never revoked in MVP 1 and never changed after its document is stored.
/// </summary>
public sealed class Certificate : AggregateRoot<CertificateId>
{
    /// <summary>
    /// Decision "Aufbewahrung" (docs/domain/README.md): pharmacies keep a certificate five years from issue; a period per
    /// pharmacy is a follow-up. The export's query in Infrastructure counts the same years (docs/adr/0034).
    /// </summary>
    public const int PharmacyRetentionYears = 5;

    private Certificate(
        CertificateId id,
        AttemptId attempt,
        PersonId holder,
        HolderName holderName,
        CourseId course,
        CourseVersionId courseVersion,
        CourseTitle courseTitle,
        CourseVersionNumber courseVersionNumber,
        ScorePercent score,
        Timestamp issuedAt,
        CalendarDate validUntil,
        VerificationCode verificationCode)
        : base(id)
    {
        Attempt = attempt;
        Holder = holder;
        HolderName = holderName;
        Course = course;
        CourseVersion = courseVersion;
        CourseTitle = courseTitle;
        CourseVersionNumber = courseVersionNumber;
        Score = score;
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
        VerificationCode = verificationCode;
        Document = DocumentState.Pending;
    }

    public AttemptId Attempt { get; }

    public PersonId Holder { get; }

    public HolderName HolderName { get; }

    public CourseId Course { get; }

    public CourseVersionId CourseVersion { get; }

    public CourseTitle CourseTitle { get; }

    public CourseVersionNumber CourseVersionNumber { get; }

    public ScorePercent Score { get; }

    public Timestamp IssuedAt { get; }

    public CalendarDate ValidUntil { get; }

    public VerificationCode VerificationCode { get; }

    public DocumentState Document { get; private set; }

    public DocumentReference? DocumentLocation { get; private set; }

    /// <summary>Issues the certificate for a passed test. Valid until the issue day plus the version's validity.</summary>
    public static Certificate Issue(
        CertificateId id,
        AttemptId attempt,
        PersonId holder,
        HolderName holderName,
        CourseId course,
        CourseVersionId courseVersion,
        CourseTitle courseTitle,
        CourseVersionNumber courseVersionNumber,
        RecurrenceInterval validity,
        ScorePercent score,
        VerificationCode verificationCode,
        Timestamp passedAt)
    {
        var validUntil = passedAt.DayInGermany().Plus(validity);
        var certificate = new Certificate(id, attempt, holder, holderName, course, courseVersion, courseTitle, courseVersionNumber, score, passedAt, validUntil, verificationCode);
        certificate.Raise(new CertificateIssued(id, holder, course, courseVersion, passedAt, validUntil));
        return certificate;
    }

    /// <summary>The rendered PDF is stored once; a second attach (a retried or replayed request) changes nothing.</summary>
    public void AttachDocument(DocumentReference location)
    {
        if (Document == DocumentState.Pending)
        {
            DocumentLocation = location;
            Document = DocumentState.Stored;
        }
    }

    public Validity ValidityOn(CalendarDate today) => today > ValidUntil ? Validity.Expired : Validity.Valid;

    /// <summary>
    /// Whether a pharmacy still sees this certificate in its view at <paramref name="now"/> (#53, docs/adr/0034).
    /// </summary>
    // stub for #391: ignores the period and keeps the five years of #53.
    public bool IsKeptByPharmaciesAt(Timestamp now, RetentionPeriod period)
    {
        Ensure.That(now).IsNotNull();
        return IssuedAt.Value.AddYears(PharmacyRetentionYears) > now.Value;
    }
}
