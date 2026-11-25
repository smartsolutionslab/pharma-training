using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

/// <summary>
/// The reviewers' and returners' names in Editorial's tables (#115): forgetting a person sets
/// <c>course_version_questions.reviewed_by_name</c> and <c>course_version_returns.returned_by_name</c> to NULL on their
/// rows, one bulk update each, outside the change tracker. It is a data-protection update of historical rows, not a change
/// of a course version: no version is loaded, its row version does not move and no domain event is raised (ADR 0036). The
/// person's id, the approval's time and the return's reason stay. The updates run on the module's context, so they join the
/// unit of work's transaction when one is open; otherwise each commits by itself, and a retry finds the names gone.
/// </summary>
public sealed class ReviewerNames(EditorialDbContext context) : IReviewerNames
{
    private readonly EditorialDbContext context = context;

    public async Task ForgetAsync(PersonId person, CancellationToken cancellationToken)
    {
        Ensure.That(person).IsNotNull();

        await context.Set<Question>()
            .Where(question => question.ReviewedBy == person && question.ReviewedByName != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(question => question.ReviewedByName, (ReviewerName?)null), cancellationToken);

        await context.Set<CourseVersionReturn>()
            .Where(record => record.ReturnedBy == person && record.ReturnedByName != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.ReturnedByName, (ReviewerName?)null), cancellationToken);
    }
}
