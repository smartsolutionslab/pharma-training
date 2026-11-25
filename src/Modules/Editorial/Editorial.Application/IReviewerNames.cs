using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The names Editorial keeps beside the people who approved a question or returned a version (#115): a data-protection
/// update of historical rows, not a change of a course version.
/// </summary>
public interface IReviewerNames
{
    /// <summary>Forgets the person's name on every question they approved and every return they made.</summary>
    Task ForgetAsync(PersonId person, CancellationToken cancellationToken);
}
