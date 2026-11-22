using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The course versions table in memory, and the module's unit of work over it: what is added is stored once committed.
/// A version found for an update is the stored one itself, so a change counts only if the handler commits it. The
/// integration events published through it are kept in <see cref="Published"/> once committed.
/// </summary>
internal sealed class InMemoryCourseVersionStore : ICourseVersionStore, IEditorialUnitOfWork
{
    private readonly List<CourseVersion> added = [];
    private readonly List<IntegrationEvent> pending = [];

    public List<CourseVersion> Stored { get; } = [];

    public int Commits { get; private set; }

    /// <summary>The integration events published through the unit of work and committed with it.</summary>
    public List<IntegrationEvent> Published { get; } = [];

    public void Add(CourseVersion version) => added.Add(version);

    public Task<Option<CourseVersion>> FindAsync(CourseVersionId id, CancellationToken cancellationToken) => Find(id);

    public Task<Option<CourseVersion>> FindForUpdateAsync(CourseVersionId id, CancellationToken cancellationToken) => Find(id);

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
        pending.Clear();
        Commits++;
        return Task.CompletedTask;
    }

    private Task<Option<CourseVersion>> Find(CourseVersionId id) =>
        Task.FromResult(Stored.SingleOrDefault(version => version.Id == id) is { } version ? Option<CourseVersion>.Some(version) : Option<CourseVersion>.None);
}
