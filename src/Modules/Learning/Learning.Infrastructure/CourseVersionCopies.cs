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
}
