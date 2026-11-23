using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure;

/// <summary>Learning's local copy of the published course versions, in its own tables; the module's unit of work saves what is added.</summary>
public sealed class CourseVersionCopies(LearningDbContext context) : ICourseVersionCopies
{
    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken)
    {
        Ensure.That(version).IsNotNull();

        return context.Set<CourseVersionCopy>().AnyAsync(copy => copy.Id == version.Value, cancellationToken);
    }

    public void Add(CourseVersionCopy version)
    {
        Ensure.That(version).IsNotNull();

        context.Add(version);
    }

    public async Task<Option<CourseVersionCopy>> FindAsync(CourseVersionId version, CancellationToken cancellationToken)
    {
        Ensure.That(version).IsNotNull();

        return await context.Set<CourseVersionCopy>()
            .AsNoTracking()
            .Include(copy => copy.Chapters.OrderBy(chapter => chapter.Position).ThenBy(chapter => chapter.ChapterId))
            .SingleOrDefaultAsync(copy => copy.Id == version.Value, cancellationToken) is { } found
            ? Option<CourseVersionCopy>.Some(found)
            : Option<CourseVersionCopy>.None;
    }

    public async Task<Option<VideoDuration>> FindVideoDurationAsync(CourseVersionId version, ChapterId chapter, CancellationToken cancellationToken)
    {
        Ensure.That(version).IsNotNull();
        Ensure.That(chapter).IsNotNull();

        var seconds = await context.Set<CourseVersionChapterCopy>()
            .AsNoTracking()
            .Where(copy => copy.CourseVersionId == version.Value && copy.ChapterId == chapter.Value && copy.Kind == ChapterKind.Video)
            .Select(copy => copy.VideoDurationSeconds)
            .SingleOrDefaultAsync(cancellationToken);
        if (seconds is { } found)
        {
            return Option<VideoDuration>.Some(VideoDuration.From(found));
        }

        return Option<VideoDuration>.None;
    }

    public async Task<IReadOnlyList<ChapterPlacement>> ListPlacementsAsync(ChapterId chapter, CancellationToken cancellationToken)
    {
        Ensure.That(chapter).IsNotNull();

        return await context.Set<CourseVersionChapterCopy>()
            .AsNoTracking()
            .Where(copied => copied.ChapterId == chapter.Value)
            .Join(
                context.Set<CourseVersionCopy>(),
                copied => copied.CourseVersionId,
                version => version.Id,
                (copied, version) => new ChapterPlacement(version.Id, version.VersionNumber, copied.Kind))
            .ToListAsync(cancellationToken);
    }
}
