using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The module's course versions with their chapters; implemented in its Infrastructure project. What a command adds
/// or changes is saved when it commits the module's unit of work.
/// </summary>
public interface ICourseVersionStore
{
    void Add(CourseVersion version);

    /// <summary>The version with its chapters in order, as it is stored, for a query: nothing that changes it is saved.</summary>
    Task<Option<CourseVersion>> FindAsync(CourseVersionId id, CancellationToken cancellationToken);

    /// <summary>The version with its chapters in order, for a command: what changes on it is saved when the unit of work commits.</summary>
    Task<Option<CourseVersion>> FindForUpdateAsync(CourseVersionId id, CancellationToken cancellationToken);

    /// <summary>One page of the versions, of one status or of all, by title, then newest number first; without their chapters.</summary>
    Task<PagedResult<CourseVersion>> ListAsync(CourseVersionStatus? status, Page page, PageSize pageSize, CancellationToken cancellationToken);
}
