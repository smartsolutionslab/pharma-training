using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Courses;

/// <summary>The course versions in memory, and the module's unit of work over them: what is added is stored only once committed.</summary>
internal sealed class InMemoryCourseVersionStore : ICourseVersionStore, ICertificationUnitOfWork
{
    private readonly List<CourseVersion> added = [];

    public List<CourseVersion> Versions { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.Any(stored => stored.Id == version));

    public void Add(CourseVersion version) => added.Add(version);

    public Task<Option<CourseVersion>> FindAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.FirstOrDefault(stored => stored.Id == version) is { } found
            ? Option<CourseVersion>.Some(found)
            : Option<CourseVersion>.None);

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Versions.AddRange(added);
        added.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
