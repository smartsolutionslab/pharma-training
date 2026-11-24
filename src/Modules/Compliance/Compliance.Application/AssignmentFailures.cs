using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>Everything the assignments' use cases and the assign wizard's member list refuse, each with its own code.</summary>
public static class AssignmentFailures
{
    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    public static ApiError PharmacyNotFound(Guid id) => new PharmacyNotFound(id);

    public static ApiError CapabilityMissing() => new CapabilityMissing();

    public static ApiError CourseMissing() => new CourseMissing();

    public static ApiError CourseNotFound(Guid id) => new CourseNotFound(id);

    public static ApiError CourseRetired(Guid id) => new CourseRetired(id);

    public static ApiError NoLearners() => new NoLearners();

    public static ApiError TooManyLearners() => new TooManyLearners();

    public static ApiError InvalidPerson() => new InvalidPerson();

    public static ApiError DueDateMissing() => new DueDateMissing();

    /// <summary>The due date lies more than <see cref="Commands.AssignCourse.AssignCourse.MaximumYearsAhead"/> years ahead of today in Germany.</summary>
    public static ApiError DueDateTooFar() => new DueDateTooFar();

    public static ApiError InvalidRecurrence() => new InvalidRecurrence();

    /// <summary>The job function asked for is a number no job function has.</summary>
    public static ApiError InvalidJobFunction() => new InvalidJobFunction();

    /// <summary>The pharmacy has no assignment of that id: unknown, or another pharmacy's.</summary>
    public static ApiError AssignmentNotFound(Guid id) => new AssignmentNotFound(id);

    /// <summary>A change's reason is longer than <see cref="ChangeReason.MaxLength"/>.</summary>
    public static ApiError InvalidChangeReason() => new InvalidChangeReason();

    /// <summary>A manager may not move or withdraw an assignment of their own (orchestrator's default on #269).</summary>
    public static ApiError OwnAssignment() => new OwnAssignment();

    public static ApiError LearnerNotAMember(Guid person) => new LearnerNotAMember(person);

    /// <summary>The save lost a race to another, or deadlocked with it; nothing of it was saved or announced.</summary>
    public static ApiError AssignmentsChanged() => new AssignmentsChanged();

    /// <summary>Another save came at the same moment and PostgreSQL ended this one; try again.</summary>
    public static ApiError ConcurrentSave() => new ComplianceConcurrentSave();

    /// <summary>How a save of assignments lost a race.</summary>
    public static ApiError LostRace(LostRace race) => race switch
    {
        VersionMovedOn => AssignmentsChanged(),
        SaveDeadlocked => ConcurrentSave(),
    };

    /// <summary>The learner already has an open or in-progress assignment of the course in the pharmacy.</summary>
    public static ApiError AlreadyActive(DomainError error) => ApiError.From(error, HttpStatusCode.Conflict);

    /// <summary>
    /// An assignment's own refusals: a due date in the past cannot be processed until it is changed, and every other
    /// refusal is a conflict with the assignment's state. The union is closed and the switch has no discard arm, so a new
    /// case fails the build until it is mapped here.
    /// </summary>
    public static ApiError From(AssignmentError error) => error switch
    {
        DueDateInThePast refusal => ApiError.From(refusal, HttpStatusCode.UnprocessableEntity),
        AssignmentNotActive refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        CertificatePredatesAssignment refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
    };
}
