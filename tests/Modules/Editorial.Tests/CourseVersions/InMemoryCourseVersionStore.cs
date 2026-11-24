using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The course versions table in memory, and the module's unit of work over it: what is added is stored once committed.
/// A version found for an update is the stored one itself, so a change counts only if the handler commits it. The
/// integration events published through it are kept in <see cref="Published"/> once committed, and so are the records of
/// the returns added through it in <see cref="Returns"/>.
/// </summary>
internal sealed class InMemoryCourseVersionStore : ICourseVersionStore, IEditorialUnitOfWork
{
    private readonly List<CourseVersion> added = [];
    private readonly List<IntegrationEvent> pending = [];
    private readonly List<CourseVersionReturn> pendingReturns = [];

    public List<CourseVersion> Stored { get; } = [];

    public int Commits { get; private set; }

    /// <summary>The commits made through <see cref="CommitOneOfSeveralAsync"/>, which <see cref="Commits"/> counts too.</summary>
    public int CommitsOfSeveral { get; private set; }

    /// <summary>The integration events published through the unit of work and committed with it.</summary>
    public List<IntegrationEvent> Published { get; } = [];

    /// <summary>The integration events of each commit, one entry per commit, in the order they were committed.</summary>
    public List<IReadOnlyList<IntegrationEvent>> PublishedPerCommit { get; } = [];

    /// <summary>The records of the returns added through the store and committed with the unit of work, in the order they were added.</summary>
    public List<CourseVersionReturn> Returns { get; } = [];

    public void Add(CourseVersion version) => added.Add(version);

    public void AddReturn(CourseVersionReturn record) => pendingReturns.Add(record);

    /// <summary>One page of the version's committed returns, newest first (then by id, newest first); none when no stored version has the id.</summary>
    public Task<Option<PagedResult<CourseVersionReturn>>> ListReturnsAsync(CourseVersionId version, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        if (!Stored.Any(stored => stored.Id == version))
        {
            return Task.FromResult(Option<PagedResult<CourseVersionReturn>>.None);
        }

        List<CourseVersionReturn> matching = [.. Returns
            .Where(record => record.Version == version)
            .OrderByDescending(record => record.ReturnedAt.Value)
            .ThenByDescending(record => record.Id.Value)];
        List<CourseVersionReturn> items = [.. matching.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
        return Task.FromResult(Option<PagedResult<CourseVersionReturn>>.Some(new PagedResult<CourseVersionReturn>(items, matching.Count, page.Value, pageSize.Value)));
    }

    public Task<Option<CourseVersion>> FindAsync(CourseVersionId id, CancellationToken cancellationToken) => Find(id);

    public Task<Option<CourseVersion>> FindForUpdateAsync(CourseVersionId id, CancellationToken cancellationToken) => Find(id);

    /// <summary>The committed version with that number for the course; what is added but not committed is not seen.</summary>
    public Task<Option<CourseVersionId>> FindByNumberAsync(CourseId course, VersionNumber number, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(version => version.Course == course && version.Number == number) is { } version
            ? Option<CourseVersionId>.Some(version.Id)
            : Option<CourseVersionId>.None);

    public Task<PagedResult<CourseVersion>> ListAsync(CourseVersionStatus? status, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<CourseVersion> matching = [.. Stored
            .Where(version => status is null || version.Status == status)
            .OrderBy(version => version.Title.Value, StringComparer.Ordinal)
            .ThenByDescending(version => version.Number.Value)
            .ThenBy(version => version.Id.Value)];
        List<CourseVersion> items = [.. matching.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
        return Task.FromResult(new PagedResult<CourseVersion>(items, matching.Count, page.Value, pageSize.Value));
    }

    /// <summary>The course's published versions, as the stored ones themselves, for a command.</summary>
    public Task<IReadOnlyList<CourseVersion>> ListPublishedForUpdateAsync(CourseId course, CancellationToken cancellationToken)
    {
        List<CourseVersion> published = [.. Stored.Where(version => version.Course == course && version.Status == CourseVersionStatus.Published)];
        return Task.FromResult<IReadOnlyList<CourseVersion>>(published);
    }

    /// <summary>
    /// The committed versions whose publish or retirement was announced, by course and then by version number; what is
    /// added but not committed is not seen.
    /// </summary>
    public Task<IReadOnlyList<CourseVersionId>> ListAnnouncedAsync(CancellationToken cancellationToken)
    {
        List<CourseVersionId> announced = [.. Stored
            .Where(version => version.PublishedAt is not null || version.RetiredAt is not null)
            .OrderBy(version => version.Course.Value)
            .ThenBy(version => version.Number.Value)
            .Select(version => version.Id)];
        return Task.FromResult<IReadOnlyList<CourseVersionId>>(announced);
    }

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        pending.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Stored.AddRange(added);
        added.Clear();
        Published.AddRange(pending);
        PublishedPerCommit.Add([.. pending]);
        pending.Clear();
        Returns.AddRange(pendingReturns);
        pendingReturns.Clear();
        Commits++;
        return Task.CompletedTask;
    }

    public Task CommitOneOfSeveralAsync(CancellationToken cancellationToken)
    {
        CommitsOfSeveral++;
        return CommitAsync(cancellationToken);
    }

    private Task<Option<CourseVersion>> Find(CourseVersionId id) =>
        Task.FromResult(Stored.SingleOrDefault(version => version.Id == id) is { } version ? Option<CourseVersion>.Some(version) : Option<CourseVersion>.None);
}
