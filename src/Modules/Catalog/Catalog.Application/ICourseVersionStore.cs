using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;

/// <summary>
/// The course versions Editorial published and the retirements of them; implemented in the module's Infrastructure
/// project. What is added is saved when the module's unit of work commits; nothing stored is ever changed.
/// </summary>
public interface ICourseVersionStore
{
    Task<bool> ExistsAsync(Guid courseVersionId, CancellationToken cancellationToken);

    void Add(CourseVersion version);

    Task<bool> IsRetiredAsync(Guid courseVersionId, CancellationToken cancellationToken);

    void Add(CourseVersionRetirement retirement);

    /// <summary>The version with its chapters, retired or not, for a query: nothing that changes it is saved.</summary>
    Task<Option<CourseVersion>> FindAsync(Guid courseVersionId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the course has a stored version with a higher version number than the one given, retired or not: that
    /// version supersedes the one given.
    /// </summary>
    Task<bool> HasNewerVersionAsync(Guid courseId, int versionNumber, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the courses as their current version, without chapters: the highest version of each course, while
    /// it is not retired, by title.
    /// </summary>
    Task<PagedResult<CourseVersion>> ListCurrentAsync(Page page, PageSize pageSize, CancellationToken cancellationToken);
}
