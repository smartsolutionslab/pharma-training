// Compliance service — Domain (module.style: ddd, module.ddd-depth: strict)
// One file for review; in the project every type lives in its own file under Domain/Assignments/.
// Root namespace is a placeholder: SmartSolutionsLab.PharmaTraining.

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

// ───────────────────────────── Identities ─────────────────────────────

public sealed record AssignmentId : IValueObject<Guid>
{
    private AssignmentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AssignmentId New() => new(Guid.CreateVersion7());

    public static AssignmentId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new AssignmentId(value);
    }

    public override string ToString() => Value.ToString();
}

// PersonId, PharmacyId, CourseId and CertificateId follow the same shape as AssignmentId
// (each service owns its own copies; there is no shared kernel across services).

public sealed record PersonId : IValueObject<Guid>
{
    private PersonId(Guid value) => Value = value;

    public Guid Value { get; }

    /// <summary>The Keycloak subject of the person.</summary>
    public static PersonId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new PersonId(value);
    }
}

public sealed record PharmacyId : IValueObject<Guid>
{
    private PharmacyId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PharmacyId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new PharmacyId(value);
    }
}

public sealed record CourseId : IValueObject<Guid>
{
    private CourseId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourseId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CourseId(value);
    }
}

public sealed record CertificateId : IValueObject<Guid>
{
    private CertificateId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CertificateId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CertificateId(value);
    }
}

// ───────────────────────────── Values ─────────────────────────────

/// <summary>A point in time, always UTC. The domain never reads a clock: callers pass it in.</summary>
public sealed record Timestamp : IValueObject<DateTimeOffset>, IComparable<Timestamp>
{
    private Timestamp(DateTimeOffset value) => Value = value;

    public DateTimeOffset Value { get; }

    public static Timestamp From(DateTimeOffset value) => new(value.ToUniversalTime());

    /// <summary>The calendar day in German local time; deadlines are days, not instants.</summary>
    public CalendarDate DayInGermany() =>
        CalendarDate.From(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Value, GermanTime).DateTime));

    public int CompareTo(Timestamp? other) => other is null ? 1 : Value.CompareTo(other.Value);

    private static readonly TimeZoneInfo GermanTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
}

public sealed record CalendarDate : IValueObject<DateOnly>, IComparable<CalendarDate>
{
    private CalendarDate(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static CalendarDate From(DateOnly value) => new(value);

    public CalendarDate Plus(RecurrenceInterval interval) => new(Value.AddMonths(interval.Value));

    public int CompareTo(CalendarDate? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(CalendarDate left, CalendarDate right) => left.CompareTo(right) < 0;

    public static bool operator >(CalendarDate left, CalendarDate right) => left.CompareTo(right) > 0;

    public static bool operator <=(CalendarDate left, CalendarDate right) => left.CompareTo(right) <= 0;

    public static bool operator >=(CalendarDate left, CalendarDate right) => left.CompareTo(right) >= 0;
}

/// <summary>The last day on which the course counts as done in time.</summary>
public sealed record DueDate : IValueObject<DateOnly>
{
    private DueDate(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static DueDate From(DateOnly value) => new(value);

    public static DueDate On(CalendarDate date) => new(date.Value);

    public CalendarDate AsDate() => CalendarDate.From(Value);

    /// <summary>Whether a completion at that instant is late: its day in Germany lies after the due date.</summary>
    public bool IsMissedBy(Timestamp completedAt) => completedAt.DayInGermany() > AsDate();
}

/// <summary>How often an assignment repeats, in months: 12 for the yearly briefing, 24 for first aid.</summary>
public sealed record RecurrenceInterval : IValueObject<int>
{
    public const int MinimumMonths = 1;
    public const int MaximumMonths = 60;

    private RecurrenceInterval(int value) => Value = value;

    public int Value { get; }

    public static RecurrenceInterval Yearly => new(12);

    public static RecurrenceInterval From(int value)
    {
        Ensure.That(value).InRange(MinimumMonths, MaximumMonths);
        return new RecurrenceInterval(value);
    }
}

/// <summary>Why a manager changed or cancelled an assignment; shown in the history.</summary>
public sealed record ChangeReason : IValueObject<string>
{
    public const int MaxLength = 500;

    private ChangeReason(string value) => Value = value;

    public string Value { get; }

    public static ChangeReason From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new ChangeReason(value);
    }

    public static ChangeReason MembershipEnded => new("Mitgliedschaft beendet");
}

// ───────────────────────────── State ─────────────────────────────

/// <summary>The stored lifecycle. Overdue and late are never stored: they follow from the due date.</summary>
public enum AssignmentStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
}

/// <summary>Whether the learner may take the test; only meaningful while in progress.</summary>
public enum TestAccess
{
    Available,
    LockedAfterFailedAttempts,
}

/// <summary>What the dashboard shows on a given day: the stored status combined with the due date.</summary>
public enum AssignmentState
{
    Open,
    InProgress,
    Locked,
    Overdue,
    Completed,
    CompletedLate,
    Cancelled,
}

public enum AssignmentChangeKind
{
    DueDateChanged,
    Cancelled,
}

/// <summary>One entry of the history a manager's changes leave behind.</summary>
public sealed record AssignmentChange(
    AssignmentChangeKind Kind,
    PersonId ChangedBy,
    Timestamp ChangedAt,
    DueDate? PreviousDueDate,
    DueDate? NewDueDate,
    ChangeReason? Reason);

// ───────────────────────────── Aggregate ─────────────────────────────

public sealed class Assignment : AggregateRoot<AssignmentId>
{
    private readonly List<AssignmentChange> history = [];

    /// <summary>For EF Core only.</summary>
    private Assignment()
        : base(default!)
    {
        Learner = default!;
        Pharmacy = default!;
        Course = default!;
        DueDate = default!;
        AssignedBy = default!;
        AssignedAt = default!;
    }

    private Assignment(
        AssignmentId id,
        PersonId learner,
        PharmacyId pharmacy,
        CourseId course,
        DueDate dueDate,
        RecurrenceInterval? recurrence,
        PersonId assignedBy,
        Timestamp assignedAt)
        : base(id)
    {
        Learner = learner;
        Pharmacy = pharmacy;
        Course = course;
        DueDate = dueDate;
        Recurrence = recurrence;
        AssignedBy = assignedBy;
        AssignedAt = assignedAt;
        Status = AssignmentStatus.Open;
        TestAccess = TestAccess.Available;
    }

    public PersonId Learner { get; }

    public PharmacyId Pharmacy { get; }

    public CourseId Course { get; }

    public DueDate DueDate { get; private set; }

    public RecurrenceInterval? Recurrence { get; }

    public PersonId AssignedBy { get; }

    public Timestamp AssignedAt { get; }

    public AssignmentStatus Status { get; private set; }

    public TestAccess TestAccess { get; private set; }

    public Timestamp? StartedAt { get; private set; }

    public Timestamp? CompletedAt { get; private set; }

    public CertificateId? Certificate { get; private set; }

    public IReadOnlyList<AssignmentChange> History => history;

    private bool IsActive => Status is AssignmentStatus.Open or AssignmentStatus.InProgress;

    /// <summary>
    /// Creates an assignment. "At most one active assignment per learner, pharmacy and course" spans aggregates:
    /// the Application checks it through the repository and a unique index backs it up.
    /// </summary>
    public static Result<Assignment, AssignmentError> Assign(
        AssignmentId id,
        PersonId learner,
        PharmacyId pharmacy,
        CourseId course,
        DueDate dueDate,
        RecurrenceInterval? recurrence,
        PersonId assignedBy,
        Timestamp now)
    {
        if (dueDate.AsDate() < now.DayInGermany())
        {
            return Failure(AssignmentErrors.DueDateInThePast(dueDate));
        }

        var assignment = new Assignment(id, learner, pharmacy, course, dueDate, recurrence, assignedBy, now);
        assignment.Raise(new CourseAssigned(id, learner, pharmacy, course, dueDate));
        return Success(assignment);
    }

    public Result<Unit, AssignmentError> ChangeDueDate(DueDate newDueDate, PersonId changedBy, ChangeReason? reason, Timestamp now)
    {
        if (!IsActive)
        {
            return Failure(AssignmentErrors.NotActive(Id, Status));
        }

        if (newDueDate.AsDate() < now.DayInGermany())
        {
            return Failure(AssignmentErrors.DueDateInThePast(newDueDate));
        }

        if (newDueDate == DueDate)
        {
            return Success(Unit.Value);
        }

        history.Add(new AssignmentChange(AssignmentChangeKind.DueDateChanged, changedBy, now, DueDate, newDueDate, reason));
        DueDate = newDueDate;
        Raise(new AssignmentDueDateChanged(Id, newDueDate));
        return Success(Unit.Value);
    }

    public Result<Unit, AssignmentError> Cancel(PersonId cancelledBy, ChangeReason reason, Timestamp now)
    {
        if (Status == AssignmentStatus.Cancelled)
        {
            return Success(Unit.Value); // MembershipEnded may arrive twice
        }

        if (!IsActive)
        {
            return Failure(AssignmentErrors.NotActive(Id, Status));
        }

        history.Add(new AssignmentChange(AssignmentChangeKind.Cancelled, cancelledBy, now, null, null, reason));
        Status = AssignmentStatus.Cancelled;
        Raise(new AssignmentCancelled(Id, Learner, Pharmacy));
        return Success(Unit.Value);
    }

    /// <summary>The learner opened the course (LearningSessionStarted). Late or repeated events change nothing.</summary>
    public void MarkStarted(Timestamp at)
    {
        if (Status == AssignmentStatus.Open)
        {
            Status = AssignmentStatus.InProgress;
            StartedAt = at;
        }

        TestAccess = TestAccess.Available; // a new session after a lock unlocks the test
    }

    /// <summary>Three failed attempts (AttemptsExhausted).</summary>
    public void MarkLocked()
    {
        if (Status == AssignmentStatus.InProgress)
        {
            TestAccess = TestAccess.LockedAfterFailedAttempts;
        }
    }

    /// <summary>
    /// A certificate for this course was issued (CertificateIssued). Allowed from Open too, because the certificate
    /// can overtake LearningSessionStarted on the bus. The same certificate twice is a no-op.
    /// </summary>
    public Result<Unit, AssignmentError> Complete(CertificateId certificate, Timestamp issuedAt)
    {
        if (Status == AssignmentStatus.Completed && Certificate == certificate)
        {
            return Success(Unit.Value);
        }

        if (!IsActive)
        {
            return Failure(AssignmentErrors.NotActive(Id, Status));
        }

        if (issuedAt.CompareTo(AssignedAt) < 0)
        {
            return Failure(AssignmentErrors.CertificatePredatesAssignment(Id, certificate));
        }

        Status = AssignmentStatus.Completed;
        TestAccess = TestAccess.Available;
        CompletedAt = issuedAt;
        Certificate = certificate;
        Raise(new AssignmentCompleted(Id, Learner, Pharmacy, Course, certificate, issuedAt, DueDate));
        return Success(Unit.Value);
    }

    /// <summary>
    /// The next assignment of a recurring one: due one interval after completion, which is the day the certificate
    /// runs out. Called by the reaction to AssignmentCompleted.
    /// </summary>
    public Option<Assignment> FollowUp(AssignmentId id, Timestamp now)
    {
        if (Status != AssignmentStatus.Completed || Recurrence is null || CompletedAt is null)
        {
            return Option<Assignment>.None;
        }

        var dueDate = DueDate.On(CompletedAt.DayInGermany().Plus(Recurrence));
        var next = new Assignment(id, Learner, Pharmacy, Course, dueDate, Recurrence, AssignedBy, now);
        next.Raise(new CourseAssigned(id, Learner, Pharmacy, Course, dueDate));
        return Option<Assignment>.Some(next);
    }

    /// <summary>What the dashboard shows today.</summary>
    public AssignmentState StateOn(CalendarDate today) =>
        Status switch
        {
            AssignmentStatus.Cancelled => AssignmentState.Cancelled,
            AssignmentStatus.Completed when DueDate.IsMissedBy(CompletedAt!) => AssignmentState.CompletedLate,
            AssignmentStatus.Completed => AssignmentState.Completed,
            _ when today > DueDate.AsDate() => AssignmentState.Overdue,
            AssignmentStatus.InProgress when TestAccess == TestAccess.LockedAfterFailedAttempts => AssignmentState.Locked,
            AssignmentStatus.InProgress => AssignmentState.InProgress,
            _ => AssignmentState.Open,
        };
}

// ───────────────────────────── Domain events ─────────────────────────────
// Turned into integration events (CourseAssignedV1, AssignmentCompletedV1) by the Application through the outbox.

[DeliberatelyUnhandled("The command that assigns announces it to other modules as CourseAssignedV1; nothing in Compliance reacts.")]
public sealed record CourseAssigned(AssignmentId Assignment, PersonId Learner, PharmacyId Pharmacy, CourseId Course, DueDate DueDate) : IDomainEvent;

[DeliberatelyUnhandled("Only the dashboard read model cares; no integration event in MVP 1.")]
public sealed record AssignmentDueDateChanged(AssignmentId Assignment, DueDate NewDueDate) : IDomainEvent;

[DeliberatelyUnhandled("Nobody outside Compliance reacts in MVP 1.")]
public sealed record AssignmentCancelled(AssignmentId Assignment, PersonId Learner, PharmacyId Pharmacy) : IDomainEvent;

[DeliberatelyUnhandled("The CertificateIssuedV1 consumer announces it as AssignmentCompletedV1 in the completing commit; #31 replaces this with the follow-up reaction.")]
public sealed record AssignmentCompleted(
    AssignmentId Assignment,
    PersonId Learner,
    PharmacyId Pharmacy,
    CourseId Course,
    CertificateId Certificate,
    Timestamp CompletedAt,
    DueDate DueDate) : IDomainEvent;

// ───────────────────────────── Domain errors ─────────────────────────────

/// <summary>Every way an assignment's own steps (Assign, ChangeDueDate, Cancel, Complete) refuse; a new one fails the build until it is mapped.</summary>
public union AssignmentError(DueDateInThePast, AssignmentNotActive, CertificatePredatesAssignment);

/// <summary>
/// The factories of the assignment's errors. The steps' errors return the union; AlreadyActive returns DomainError, since
/// no aggregate method returns it: the Application checks it across aggregates.
/// </summary>
public static class AssignmentErrors
{
    public static AssignmentError DueDateInThePast(DueDate dueDate) => new DueDateInThePast(dueDate);

    public static AssignmentError NotActive(AssignmentId assignment, AssignmentStatus status) => new AssignmentNotActive(assignment, status);

    public static AssignmentError CertificatePredatesAssignment(AssignmentId assignment, CertificateId certificate) =>
        new CertificatePredatesAssignment(assignment, certificate);

    public static DomainError AlreadyActive(PersonId learner, CourseId course) => new AssignmentAlreadyActive(learner, course);
}

public sealed record DueDateInThePast(DueDate DueDate)
    : DomainError("compliance.due-date-in-the-past", $"Die Frist {DueDate.Value:dd.MM.yyyy} liegt in der Vergangenheit.");

public sealed record AssignmentNotActive(AssignmentId Assignment, AssignmentStatus Status)
    : DomainError("compliance.assignment-not-active", $"Die Zuweisung ist bereits {Status}.");

public sealed record CertificatePredatesAssignment(AssignmentId Assignment, CertificateId Certificate)
    : DomainError("compliance.certificate-predates-assignment", "Eine Bestätigung von vor der Zuweisung erfüllt sie nicht.");

public sealed record AssignmentAlreadyActive(PersonId Learner, CourseId Course)
    : DomainError("compliance.assignment-already-active", "Für diese Schulung gibt es schon eine offene Zuweisung.");
