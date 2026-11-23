using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>
/// Proof that a person passed one course version. Issued once per passed attempt (a unique index on the attempt makes
/// a redelivered TestPassed harmless), never revoked in MVP 1 and never changed after its document is stored.
/// </summary>
public sealed class Certificate : AggregateRoot<CertificateId>
{
    private Certificate(
        CertificateId id,
        AttemptId attempt,
        PersonId holder,
        HolderName holderName,
        CourseId course,
        CourseVersionId courseVersion,
        CourseTitle courseTitle,
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
        RecurrenceInterval validity,
        ScorePercent score,
        VerificationCode verificationCode,
        Timestamp passedAt)
    {
        var validUntil = passedAt.DayInGermany().Plus(validity);
        var certificate = new Certificate(id, attempt, holder, holderName, course, courseVersion, courseTitle, score, passedAt, validUntil, verificationCode);
        certificate.Raise(new CertificateIssued(id, holder, course, courseVersion, passedAt, validUntil));
        return certificate;
    }

    /// <summary>The rendered PDF is stored once; a second render (a retried reaction) changes nothing.</summary>
    public void AttachDocument(DocumentReference location)
    {
        if (Document == DocumentState.Pending)
        {
            DocumentLocation = location;
            Document = DocumentState.Stored;
        }
    }

    public Validity ValidityOn(CalendarDate today) => today > ValidUntil ? Validity.Expired : Validity.Valid;
}
