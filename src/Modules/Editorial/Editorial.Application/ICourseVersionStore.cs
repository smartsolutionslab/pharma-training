using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The module's course versions with their chapters; implemented in its Infrastructure project. What a command adds
/// or changes is saved when it commits the module's unit of work.
/// </summary>
public interface ICourseVersionStore
{
    void Add(CourseVersion version);

    /// <summary>The record of a return, saved with the version when the unit of work commits.</summary>
    void AddReturn(CourseVersionReturn record);

    /// <summary>
    /// One page of the version's returns, newest first, then by id, newest first; none when no such version exists.
    /// </summary>
    Task<Option<PagedResult<CourseVersionReturn>>> ListReturnsAsync(CourseVersionId version, Page page, PageSize pageSize, CancellationToken cancellationToken);

    /// <summary>The version with its chapters in order, as it is stored, for a query: nothing that changes it is saved.</summary>
    Task<Option<CourseVersion>> FindAsync(CourseVersionId id, CancellationToken cancellationToken);

    /// <summary>
    /// The version with its chapters in order, for a command: what changes on it is saved when the unit of work commits.
    /// Both finds read in a transaction of their own (one snapshot for the split queries), so neither may be called inside
    /// a transaction the caller opened.
    /// </summary>
    Task<Option<CourseVersion>> FindForUpdateAsync(CourseVersionId id, CancellationToken cancellationToken);

    /// <summary>The version stored with that number for the course, read afresh; none while there is none.</summary>
    Task<Option<CourseVersionId>> FindByNumberAsync(CourseId course, VersionNumber number, CancellationToken cancellationToken);

    /// <summary>One page of the versions, of one status or of all, by title, then newest number first; without their chapters.</summary>
    Task<PagedResult<CourseVersion>> ListAsync(CourseVersionStatus? status, Page page, PageSize pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// The versions whose publish or retirement was announced, those with <c>PublishedAt</c> or <c>RetiredAt</c> set, by
    /// course and then by version number. Never selected by status: a version marked published by any other way was never
    /// announced.
    /// </summary>
    Task<IReadOnlyList<CourseVersionId>> ListAnnouncedAsync(CancellationToken cancellationToken);

    /// <summary>The course's published versions, without their content, for a command: what changes on them is saved when the unit of work commits.</summary>
    Task<IReadOnlyList<CourseVersion>> ListPublishedForUpdateAsync(CourseId course, CancellationToken cancellationToken);
}
