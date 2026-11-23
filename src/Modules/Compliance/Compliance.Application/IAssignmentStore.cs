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
    /// <summary>Which of the learners already have an open or in-progress assignment of the course in the pharmacy.</summary>
    Task<IReadOnlySet<PersonId>> ActiveLearnersAsync(PharmacyId pharmacy, CourseId course, IReadOnlyCollection<PersonId> learners, CancellationToken cancellationToken);

    void Add(Assignment assignment);
}
