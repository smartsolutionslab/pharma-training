using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;

/// <summary>
/// The course versions and their retirements in memory, and the module's unit of work over them: what is added is
/// stored only once committed. A course is read as the store in Infrastructure reads it, through
/// <see cref="PublishedCourse.Of"/> over that course's versions and retirements.
/// </summary>
internal sealed class InMemoryCourseVersionStore : ICourseVersionStore, IComplianceUnitOfWork
{
    private readonly List<CourseVersion> addedVersions = [];
    private readonly List<CourseVersionRetirement> addedRetirements = [];

    public List<CourseVersion> Versions { get; } = [];

    public List<CourseVersionRetirement> Retirements { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.Any(stored => stored.Id == version));

    public Task<Option<CourseVersionId>> FindByNumberAsync(CourseId course, VersionNumber number, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.FirstOrDefault(stored => stored.Course == course && stored.Number == number) is { } version
            ? Option<CourseVersionId>.Some(version.Id)
            : Option<CourseVersionId>.None);

    public void Add(CourseVersion version) => addedVersions.Add(version);

    public Task<bool> IsRetiredAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Retirements.Any(retirement => retirement.Id == version));

    public void Add(CourseVersionRetirement retirement) => addedRetirements.Add(retirement);

    public Task<Option<PublishedCourse>> FindCourseAsync(CourseId course, CancellationToken cancellationToken)
    {
        List<CourseVersion> versions = [.. Versions.Where(version => version.Course == course)];
        List<CourseVersionRetirement> retirements = [.. Retirements.Where(retirement => retirement.Course == course)];
        return Task.FromResult(PublishedCourse.Of(course, versions, retirements));
    }

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Versions.AddRange(addedVersions);
        addedVersions.Clear();
        Retirements.AddRange(addedRetirements);
        addedRetirements.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
