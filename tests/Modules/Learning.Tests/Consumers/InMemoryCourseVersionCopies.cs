using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>Learning's local copy of the published course versions in memory, and its unit of work: what is added is stored only once committed.</summary>
internal sealed class InMemoryCourseVersionCopies : ICourseVersionCopies, ILearningUnitOfWork
{
    private readonly List<CourseVersionCopy> added = [];

    public List<CourseVersionCopy> Versions { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.Any(stored => stored.Id == version.Value));

    public void Add(CourseVersionCopy version) => added.Add(version);

    public Task<Option<CourseVersionCopy>> FindAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.FirstOrDefault(stored => stored.Id == version.Value) is { } found
            ? Option<CourseVersionCopy>.Some(found)
            : Option<CourseVersionCopy>.None);

    public Task<Option<VideoDuration>> FindVideoDurationAsync(CourseVersionId version, ChapterId chapter, CancellationToken cancellationToken) =>
        Task.FromResult(Versions
            .Where(stored => stored.Id == version.Value)
            .SelectMany(stored => stored.Chapters)
            .FirstOrDefault(stored => stored.ChapterId == chapter.Value && stored.Kind == ChapterKind.Video) is { VideoDurationSeconds: { } seconds }
            ? Option<VideoDuration>.Some(VideoDuration.From(seconds))
            : Option<VideoDuration>.None);

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

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Versions.AddRange(added);
        added.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
