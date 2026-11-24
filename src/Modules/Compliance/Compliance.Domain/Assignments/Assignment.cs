using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>
/// A course a learner must complete in a pharmacy by a due date, perhaps again and again. Which state it shows on a day
/// follows from its stored status and the due date (<see cref="StateOn"/>).
/// </summary>
public sealed class Assignment : AggregateRoot<AssignmentId>
{
    private readonly List<AssignmentChange> history = [];

    private Assignment(
        AssignmentId id,
        PersonId learner,
        PharmacyId pharmacy,
        CourseId course,
        DueDate dueDate,
        RecurrenceInterval? recurrence,
        PersonId assignedBy,
        Timestamp assignedAt,
        AssignmentId? follows)
        : this(id)
    {
        Learner = learner;
        Pharmacy = pharmacy;
        Course = course;
        DueDate = dueDate;
        Recurrence = recurrence;
        AssignedBy = assignedBy;
        AssignedAt = assignedAt;
        Follows = follows;
        Status = AssignmentStatus.Open;
        TestAccess = TestAccess.Available;
    }

    /// <summary>For EF Core, which sets the rest through the backing fields.</summary>
    private Assignment(AssignmentId id)
        : base(id)
    {
    }

    public PersonId Learner { get; } = default!;

    public PharmacyId Pharmacy { get; } = default!;

    public CourseId Course { get; } = default!;

    public DueDate DueDate { get; private set; } = default!;

    public RecurrenceInterval? Recurrence { get; }

    public PersonId AssignedBy { get; } = default!;

    public Timestamp AssignedAt { get; } = default!;

    public AssignmentStatus Status { get; private set; }

    public TestAccess TestAccess { get; private set; }

    public Timestamp? StartedAt { get; private set; }

    /// <summary>When the lock or unlock last applied to the test happened; null until the first (docs/adr/0023).</summary>
    public Timestamp? TestAccessChangedAt { get; private set; }

    public Timestamp? CompletedAt { get; private set; }

    public CertificateId? Certificate { get; private set; }

    /// <summary>The assignment this one follows up, when it was created by <see cref="FollowUp"/>; null for one assigned by hand.</summary>
    public AssignmentId? Follows { get; }

    /// <summary>A read-only view: a cast does not lead back to the list behind it.</summary>
    public IReadOnlyList<AssignmentChange> History => history.AsReadOnly();

    private bool IsActive => Status is AssignmentStatus.Open or AssignmentStatus.InProgress;

    /// <summary>
    /// Creates an assignment. "At most one active assignment per learner, pharmacy and course" spans aggregates:
    /// the Application checks it through the store and a unique index backs it up.
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
        Ensure.That(id).IsNotNull();
        Ensure.That(learner).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(dueDate).IsNotNull();
        Ensure.That(assignedBy).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (dueDate.AsDate() < now.DayInGermany())
        {
            return Failure(AssignmentErrors.DueDateInThePast(dueDate));
        }

        var assignment = new Assignment(id, learner, pharmacy, course, dueDate, recurrence, assignedBy, now, null);
        assignment.Raise(new CourseAssigned(id, learner, pharmacy, course, dueDate));
        return Success(assignment);
    }

    public Result<Unit, AssignmentError> ChangeDueDate(DueDate newDueDate, PersonId changedBy, ChangeReason? reason, Timestamp now)
    {
        Ensure.That(newDueDate).IsNotNull();
        Ensure.That(changedBy).IsNotNull();
        Ensure.That(now).IsNotNull();

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
        Ensure.That(cancelledBy).IsNotNull();
        Ensure.That(reason).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (Status == AssignmentStatus.Cancelled)
        {
            // MembershipEnded may arrive twice.
            return Success(Unit.Value);
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

    /// <summary>
    /// The learner opened the course (LearningSessionStarted): an open assignment goes in progress. The earliest start
    /// wins, so the order the starts arrive in does not matter. A start before the assignment was assigned counts too
    /// (owner decision on #247): Learning announces a session once, so a learner who began earlier is learning all the
    /// same. A start of a completed or cancelled assignment changes nothing. A start no longer unlocks the test: only
    /// relearned content does (<see cref="Unlock"/>, docs/adr/0023).
    /// </summary>
    public void MarkStarted(Timestamp at)
    {
        Ensure.That(at).IsNotNull();

        if (!IsActive)
        {
            return;
        }

        if (Status == AssignmentStatus.Open)
        {
            Status = AssignmentStatus.InProgress;
            StartedAt = at;
        }
        else if (StartedAt is null || at < StartedAt)
        {
            StartedAt = at;
        }
    }

    /// <summary>
    /// Three failed attempts (AttemptsExhausted) lock the test, unless a lock or unlock that happened later was applied
    /// already; on the same instant the lock wins (docs/adr/0023). A lock while the assignment is open is kept and shows
    /// once it is in progress.
    /// </summary>
    public void MarkLocked(Timestamp at)
    {
        Ensure.That(at).IsNotNull();

        if (!IsActive || at < AssignedAt || (TestAccessChangedAt is not null && at < TestAccessChangedAt))
        {
            return;
        }

        TestAccess = TestAccess.LockedAfterFailedAttempts;
        TestAccessChangedAt = at;
    }

    /// <summary>
    /// The learner worked through the content again (ContentCompleted): the test is available, unless a lock or unlock
    /// that happened at the same time or later was applied already (docs/adr/0023). It never changes the status.
    /// </summary>
    public void Unlock(Timestamp at)
    {
        Ensure.That(at).IsNotNull();

        if (!IsActive || at < AssignedAt || (TestAccessChangedAt is not null && at <= TestAccessChangedAt))
        {
            return;
        }

        TestAccess = TestAccess.Available;
        TestAccessChangedAt = at;
    }

    /// <summary>
    /// A certificate for this course was issued (CertificateIssued). Allowed from Open too, because the certificate
    /// can overtake LearningSessionStarted on the bus. The same certificate twice is a no-op. A follow-up refuses a
    /// certificate issued at or before it was assigned, so the certificate that completed its predecessor never completes
    /// it, whatever the two services' clocks say (docs/adr/0026).
    /// </summary>
    public Result<Unit, AssignmentError> Complete(CertificateId certificate, Timestamp issuedAt)
    {
        Ensure.That(certificate).IsNotNull();
        Ensure.That(issuedAt).IsNotNull();

        if (Status == AssignmentStatus.Completed && Certificate == certificate)
        {
            return Success(Unit.Value);
        }

        if (!IsActive)
        {
            return Failure(AssignmentErrors.NotActive(Id, Status));
        }

        if (Predates(issuedAt))
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
    /// runs out. It records the assignment it follows and is never assigned before the completion it follows. Called by
    /// the consumer of the follow-up request (docs/adr/0026).
    /// </summary>
    public Option<Assignment> FollowUp(AssignmentId id, Timestamp now)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (Status != AssignmentStatus.Completed || Recurrence is null || CompletedAt is null)
        {
            return Option<Assignment>.None;
        }

        var dueDate = DueDate.On(CompletedAt.DayInGermany().Plus(Recurrence));
        var assignedAt = now > CompletedAt ? now : CompletedAt;
        var next = new Assignment(id, Learner, Pharmacy, Course, dueDate, Recurrence, AssignedBy, assignedAt, Id);
        next.Raise(new CourseAssigned(id, Learner, Pharmacy, Course, dueDate));
        return Option<Assignment>.Some(next);
    }

    /// <summary>What the dashboard shows on that day.</summary>
    public AssignmentState StateOn(CalendarDate today)
    {
        Ensure.That(today).IsNotNull();

        return Status switch
        {
            AssignmentStatus.Cancelled => AssignmentState.Cancelled,
            AssignmentStatus.Completed when CompletedAt is not null && DueDate.IsMissedBy(CompletedAt) => AssignmentState.CompletedLate,
            AssignmentStatus.Completed => AssignmentState.Completed,
            _ when today > DueDate.AsDate() => AssignmentState.Overdue,
            AssignmentStatus.InProgress when TestAccess == TestAccess.LockedAfterFailedAttempts => AssignmentState.Locked,
            AssignmentStatus.InProgress => AssignmentState.InProgress,
            _ => AssignmentState.Open,
        };
    }

    private bool Predates(Timestamp issuedAt) => Follows is null ? issuedAt < AssignedAt : issuedAt <= AssignedAt;
}
