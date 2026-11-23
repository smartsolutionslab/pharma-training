using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>The sessions' views, as the inline projection keeps them; implemented in Learning's Infrastructure project over its event store.</summary>
public interface ILearningSessions
{
    /// <summary>Every run of the learner on the course version, finished or not.</summary>
    Task<IReadOnlyList<LearningSessionView>> ListAsync(PersonId learner, CourseVersionId version, CancellationToken cancellationToken);

    Task<Option<LearningSessionView>> FindAsync(LearningSessionId id, CancellationToken cancellationToken);
}
