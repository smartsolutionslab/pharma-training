using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>Learning's local copy of the published course versions; implemented in its Infrastructure project.</summary>
public interface ICourseVersionCopies
{
    Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken);

    /// <summary>Adds the copy; committing the module's unit of work stores it.</summary>
    void Add(CourseVersionCopy version);

    /// <summary>The copy with its chapters, or none while Learning has not received the version.</summary>
    Task<Option<CourseVersionCopy>> FindAsync(CourseVersionId version, CancellationToken cancellationToken);
}
