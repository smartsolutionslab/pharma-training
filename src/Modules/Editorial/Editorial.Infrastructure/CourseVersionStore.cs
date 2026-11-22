using System.Data;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

/// <summary>The course versions with their chapters and questions. The module's unit of work saves what is added or changed.</summary>
public sealed class CourseVersionStore(EditorialDbContext context) : ICourseVersionStore
{
    public void Add(CourseVersion version) => context.Add(version);

    /// <summary>
    /// The version as it is read back: its split queries run in one REPEATABLE READ transaction, so they read one
    /// snapshot, and a change committed between them (a chapter removed, a question's options replaced) shows in none
    /// of them or in all. A write loads with <see cref="FindForUpdateAsync"/>, whose row version comes from its first
    /// query.
    /// </summary>
    public Task<Option<CourseVersion>> FindAsync(CourseVersionId id, CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(
            async cancellation =>
            {
                await using var snapshot = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellation);
                var found = await FindAsync(WithContent().AsNoTracking(), id, cancellation);
                await snapshot.CommitAsync(cancellation);
                return found;
            },
            cancellationToken);

    public Task<Option<CourseVersion>> FindForUpdateAsync(CourseVersionId id, CancellationToken cancellationToken) =>
        FindAsync(WithContent(), id, cancellationToken);

    public async Task<PagedResult<CourseVersion>> ListAsync(CourseVersionStatus? status, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var versions = context.Set<CourseVersion>().AsNoTracking();
        if (status is { } wanted)
        {
            versions = versions.Where(version => version.Status == wanted);
        }

        var total = await versions.CountAsync(cancellationToken);
        var items = await versions
            .OrderBy(version => version.Title)
            .ThenByDescending(version => version.Number)
            .ThenBy(version => version.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return new PagedResult<CourseVersion>(items, total, page.Value, pageSize.Value);
    }

    private static async Task<Option<CourseVersion>> FindAsync(IQueryable<CourseVersion> versions, CourseVersionId id, CancellationToken cancellationToken) =>
        await versions.SingleOrDefaultAsync(version => version.Id == id, cancellationToken) is { } version
            ? Option<CourseVersion>.Some(version)
            : Option<CourseVersion>.None;

    /// <summary>
    /// The version with its chapters, its questions and their options, each in its order. Split into one query per
    /// collection, so the chapters are not repeated for every question and option.
    /// </summary>
    private IQueryable<CourseVersion> WithContent() =>
        context.Set<CourseVersion>()
            .Include(version => version.Chapters.OrderBy(chapter => EF.Property<int>(chapter, ChapterConfiguration.Position)))
            .Include(version => version.Questions.OrderBy(question => EF.Property<int>(question, QuestionConfiguration.Position)))
            .ThenInclude(question => question.Options.OrderBy(option => EF.Property<int>(option, AnswerOptionConfiguration.Position)))
            .AsSplitQuery();
}
