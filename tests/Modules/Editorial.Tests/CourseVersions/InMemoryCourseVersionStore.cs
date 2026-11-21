using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The course versions table in memory, and the module's unit of work over it: what is added is stored once committed.
/// A version found for an update is the stored one itself, so a change counts only if the handler commits it.
/// </summary>
internal sealed class InMemoryCourseVersionStore : ICourseVersionStore, IEditorialUnitOfWork
{
    private readonly List<CourseVersion> added = [];

    public List<CourseVersion> Stored { get; } = [];

    public int Commits { get; private set; }

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

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Stored.AddRange(added);
        added.Clear();
        Commits++;
        return Task.CompletedTask;
    }

    private Task<Option<CourseVersion>> Find(CourseVersionId id) =>
        Task.FromResult(Stored.SingleOrDefault(version => version.Id == id) is { } version ? Option<CourseVersion>.Some(version) : Option<CourseVersion>.None);
}
