using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// The module's assignments; implemented in its Infrastructure project. What is added is saved when the module's unit
/// of work commits.
/// </summary>
public interface IAssignmentStore
{
    /// <summary>The assignment, whatever its status or pharmacy, read without tracking.</summary>
    Task<Option<Assignment>> ReadAsync(AssignmentId assignment, CancellationToken cancellationToken);

    /// <summary>Whether an assignment that follows up this one is stored, whatever its status.</summary>
    Task<bool> HasFollowUpAsync(AssignmentId assignment, CancellationToken cancellationToken);

    /// <summary>Every assignment in the pharmacy, whatever its status or learner, read without tracking.</summary>
    Task<IReadOnlyList<Assignment>> InPharmacyAsync(PharmacyId pharmacy, CancellationToken cancellationToken);

    /// <summary>Which of the learners already have an open or in-progress assignment of the course in the pharmacy.</summary>
    Task<IReadOnlySet<PersonId>> ActiveLearnersAsync(PharmacyId pharmacy, CourseId course, IReadOnlyCollection<PersonId> learners, CancellationToken cancellationToken);

    /// <summary>The learner's open or in-progress assignments of the course in every pharmacy, tracked so a commit saves their changes.</summary>
    Task<IReadOnlyList<Assignment>> ActiveOfLearnerAsync(PersonId learner, CourseId course, CancellationToken cancellationToken);

    void Add(Assignment assignment);

    /// <summary>The assignment if it belongs to the pharmacy, tracked so a commit saves its changes.</summary>
    Task<Option<Assignment>> FindInPharmacyAsync(PharmacyId pharmacy, AssignmentId assignment, CancellationToken cancellationToken);

    /// <summary>The assignment if it belongs to the pharmacy, read without tracking.</summary>
    Task<Option<Assignment>> ReadInPharmacyAsync(PharmacyId pharmacy, AssignmentId assignment, CancellationToken cancellationToken);

    /// <summary>
    /// The learner's open or in-progress assignments in the pharmacy, every course, tracked so a commit saves the changes
    /// made to them: for a use case that changes them, such as cancelling on leaving. A query reads
    /// <see cref="ReadActiveOfLearnerInPharmacyAsync"/> instead.
    /// </summary>
    Task<IReadOnlyList<Assignment>> ActiveOfLearnerInPharmacyAsync(PersonId learner, PharmacyId pharmacy, CancellationToken cancellationToken);

    /// <summary>The learner's open or in-progress assignments in the pharmacy, every course, read without tracking, for a query.</summary>
    Task<IReadOnlyList<Assignment>> ReadActiveOfLearnerInPharmacyAsync(PersonId learner, PharmacyId pharmacy, CancellationToken cancellationToken);

    /// <summary>The learner's open or in-progress assignments in every pharmacy, every course, tracked so a commit saves their changes.</summary>
    Task<IReadOnlyList<Assignment>> ActiveOfLearnerInEveryPharmacyAsync(PersonId learner, CancellationToken cancellationToken);
}
