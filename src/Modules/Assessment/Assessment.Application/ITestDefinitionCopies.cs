using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>
/// Assessment's local copy of the published test definitions; implemented in its Infrastructure project. It carries
/// the solutions, so whatever reads it for a learner projects a shape without correctness.
/// </summary>
public interface ITestDefinitionCopies
{
    Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken);

    /// <summary>Adds the copy; committing the module's unit of work stores it.</summary>
    void Add(TestDefinitionCopy definition);
}
