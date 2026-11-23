using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>Learning's local copy of the published course versions and their retirements; implemented in its Infrastructure project.</summary>
public interface ICourseVersionCopies
{
    Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken);

    /// <summary>Adds the copy; committing the module's unit of work stores it.</summary>
    void Add(CourseVersionCopy version);

    /// <summary>The copy with its chapters, or none while Learning has not received the version.</summary>
    Task<Option<CourseVersionCopy>> FindAsync(CourseVersionId version, CancellationToken cancellationToken);

    /// <summary>Whether Editorial retired the version, whether or not Learning holds the version itself yet.</summary>
    Task<bool> IsRetiredAsync(CourseVersionId version, CancellationToken cancellationToken);

    /// <summary>Adds the retirement; committing the module's unit of work stores it.</summary>
    void Add(CourseVersionRetirement retirement);

    /// <summary>Every version Learning holds with the chapter, with its number and the chapter's kind there; none when it holds the chapter in no version.</summary>
    Task<IReadOnlyList<ChapterPlacement>> ListPlacementsAsync(ChapterId chapter, CancellationToken cancellationToken);
}
