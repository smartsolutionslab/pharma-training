using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Learning's local copy of the published course versions and their retirements in memory, and its unit of work: what
/// is added is stored only once committed.
/// </summary>
internal sealed class InMemoryCourseVersionCopies : ICourseVersionCopies, ILearningUnitOfWork
{
    private readonly List<CourseVersionCopy> added = [];

    private readonly List<CourseVersionRetirement> addedRetirements = [];

    public List<CourseVersionCopy> Versions { get; } = [];

    public List<CourseVersionRetirement> Retirements { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.Any(stored => stored.Id == version.Value));

    public void Add(CourseVersionCopy version) => added.Add(version);

    public Task<Option<CourseVersionCopy>> FindAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.FirstOrDefault(stored => stored.Id == version.Value) is { } found
            ? Option<CourseVersionCopy>.Some(found)
            : Option<CourseVersionCopy>.None);

    public Task<bool> IsRetiredAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Retirements.Any(stored => stored.CourseVersionId == version.Value));

    public void Add(CourseVersionRetirement retirement) => addedRetirements.Add(retirement);

    /// <summary>Every version Learning holds with the chapter, by its number and the chapter's kind there (#41).</summary>
    public Task<IReadOnlyList<ChapterPlacement>> ListPlacementsAsync(ChapterId chapter, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChapterPlacement> placements = [.. Versions
            .SelectMany(version => version.Chapters
                .Where(copied => copied.ChapterId == chapter.Value)
                .Select(copied => new ChapterPlacement(version.Id, version.VersionNumber, copied.Kind)))];
        return Task.FromResult(placements);
    }

    /// <summary>Stores a copy as if its event had been handled, for tests that start from a version Learning holds.</summary>
    public void Hold(CourseVersionCopy version) => Versions.Add(version);

    /// <summary>Stores a retirement as if its event had been handled, for tests that start from a version Learning knows is retired.</summary>
    public void Retire(CourseVersionRetirement retirement) => Retirements.Add(retirement);

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Versions.AddRange(added);
        added.Clear();
        Retirements.AddRange(addedRetirements);
        addedRetirements.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
