using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>Everything the assignments' use cases refuse, each with its own code.</summary>
public static class AssignmentFailures
{
    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    public static ApiError PharmacyNotFound(Guid id) => new PharmacyNotFound(id);

    public static ApiError CapabilityMissing() => new CapabilityMissing();

    public static ApiError CourseNotFound(Guid id) => new CourseNotFound(id);

    public static ApiError CourseRetired(Guid id) => new CourseRetired(id);

    public static ApiError NoLearners() => new NoLearners();

    public static ApiError TooManyLearners(int maximum) => new TooManyLearners(maximum);

    public static ApiError InvalidPerson() => new InvalidPerson();

    public static ApiError DueDateMissing() => new DueDateMissing();

    public static ApiError InvalidRecurrence() => new InvalidRecurrence(RecurrenceInterval.MinimumMonths, RecurrenceInterval.MaximumMonths);

    public static ApiError LearnerNotAMember(Guid person) => new LearnerNotAMember(person);

    /// <summary>The save lost a race to another, or deadlocked with it; nothing of it was saved or announced.</summary>
    public static ApiError AssignmentsChanged() => new AssignmentsChanged();

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
