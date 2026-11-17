// Certification service — Domain (module.style: ddd, module.ddd-depth: strict)
// One file for review; in the project every type lives in its own file under Domain/Certificates/.
// Identities (CertificateId, PersonId, CourseId, CourseVersionId, AttemptId), Timestamp, CalendarDate,
// ScorePercent and RecurrenceInterval: same shape as in the other files.
//
// Certification keeps local copies it needs to issue: the person's name (from MembershipStarted) and the
// course version's title and validity (from CourseVersionPublished). The Application hands them in.

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

// ───────────────────────────── Values ─────────────────────────────

/// <summary>The holder's name as it stood when the certificate was issued; it never changes afterwards.</summary>
public sealed record HolderName : IValueObject<string>
{
    public const int MaxLength = 100;

    private HolderName(string value) => Value = value;

    public string Value { get; }

    public static HolderName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new HolderName(value.Trim());
    }
}

public sealed record CourseTitle : IValueObject<string>
{
    public const int MaxLength = 150;

    private CourseTitle(string value) => Value = value;

    public string Value { get; }

    public static CourseTitle From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new CourseTitle(value.Trim());
    }
}

/// <summary>
/// The code in the QR link. Random and long, created by the Application from a cryptographic source, and never the
/// certificate's id: knowing one certificate must not let anyone guess another.
/// </summary>
public sealed record VerificationCode : IValueObject<string>
{
    public const int MinLength = 24;
    public const int MaxLength = 64;

    private VerificationCode(string value) => Value = value;

    public string Value { get; }

    public static VerificationCode From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMinLength(MinLength).HasMaxLength(MaxLength);
        return new VerificationCode(value);
    }
}

/// <summary>Where the rendered PDF lies in blob storage.</summary>
public sealed record DocumentReference : IValueObject<string>
{
    public const int MaxLength = 1024;

    private DocumentReference(string value) => Value = value;

    public string Value { get; }

    public static DocumentReference From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new DocumentReference(value);
    }
}

/// <summary>Whether the PDF exists yet. It is rendered once, right after issuing, by a reaction.</summary>
public enum DocumentState
{
    Pending,
    Stored,
}

/// <summary>What a verifier sees for a code on a given day.</summary>
public enum Validity
{
    Valid,
    Expired,
}

// ───────────────────────────── Aggregate ─────────────────────────────

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

// ───────────────────────────── Domain events ─────────────────────────────
// CertificateIssued → integration event CertificateIssuedV1 (Compliance) and a reaction that renders and stores the PDF.

public sealed record CertificateIssued(
    CertificateId Certificate,
    PersonId Holder,
    CourseId Course,
    CourseVersionId CourseVersion,
    Timestamp IssuedAt,
    CalendarDate ValidUntil) : IDomainEvent;
