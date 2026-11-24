using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.CourseVersions;

/// <summary>
/// The course versions and their retirements in memory, and the module's unit of work over them: what is added is
/// stored only once committed. The current versions are listed as the store in Infrastructure lists them: the highest
/// version of each course, while it is not retired, by title.
/// </summary>
internal sealed class InMemoryCourseVersionStore : ICourseVersionStore, ICatalogUnitOfWork
{
    private readonly List<CourseVersion> addedVersions = [];
    private readonly List<CourseVersionRetirement> addedRetirements = [];

    public List<CourseVersion> Stored { get; } = [];

    public List<CourseVersionRetirement> Retirements { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(version => version.Id == courseVersionId));

    public void Add(CourseVersion version) => addedVersions.Add(version);

    public Task<bool> IsRetiredAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        Task.FromResult(Retirements.Any(retirement => retirement.CourseVersionId == courseVersionId));

    public void Add(CourseVersionRetirement retirement) => addedRetirements.Add(retirement);

    public Task<Option<CourseVersion>> FindAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(version => version.Id == courseVersionId) is { } version ? Option<CourseVersion>.Some(version) : Option<CourseVersion>.None);

    public Task<bool> HasNewerVersionAsync(Guid courseId, int versionNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(version => version.CourseId == courseId && version.VersionNumber > versionNumber));

    public Task<PagedResult<CourseVersion>> ListCurrentAsync(Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<CourseVersion> current = [.. Stored
            .Where(version => !Retirements.Any(retirement => retirement.CourseVersionId == version.Id))
            .Where(version => !Stored.Any(other => other.CourseId == version.CourseId && other.VersionNumber > version.VersionNumber))
            .OrderBy(version => version.Title, StringComparer.Ordinal)
            .ThenBy(version => version.CourseId)];
        List<CourseVersion> items = [.. current.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
        return Task.FromResult(new PagedResult<CourseVersion>(items, current.Count, page.Value, pageSize.Value));
    }

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Stored.AddRange(addedVersions);
        addedVersions.Clear();
        Retirements.AddRange(addedRetirements);
        addedRetirements.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
