using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.DraftNextVersion;

/// <summary>
/// Drafts the next version of a published one, numbered one higher. A version has one successor at most: a second is
/// refused while the first exists, so a course has at most one unpublished version at any time, and two first
/// publishes of one course cannot race. Two drafts that race past the check meet at the unique (course, number)
/// index, which the host answers with 409 <c>already-exists</c>; that violation is never caught here. A question approved
/// by a person Editorial has erased (#115) is copied approved, under their id, without their name.
/// </summary>
public sealed class DraftNextVersionHandler(ICourseVersionStore versions, IErasedPersonStore erased, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<DraftNextVersion, Result<Guid, ApiError>>
{
    private readonly IErasedPersonStore erased = erased;

    public async Task<Result<Guid, ApiError>> HandleAsync(DraftNextVersion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var source = found.Value;
        var erasedReviewers = await ErasedReviewersAsync(source, cancellationToken);
        var drafted = source.DraftNext(CourseVersionId.New(), command.Editor, ChapterId.New, QuestionId.New, AnswerOptionId.New, erasedReviewers);
        if (drafted.IsFailure)
        {
            return Failure(CourseVersionFailures.From(drafted.Error));
        }

        var next = drafted.Value;
        var existing = await versions.FindByNumberAsync(next.Course, next.Number, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(CourseVersionFailures.NextVersionExists());
        }

        versions.Add(next);
        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            // The commit lost (a deadlock or a serialization failure). If another editor drafted this number meanwhile,
            // say so; a unique violation never reaches here, it answers 409 already-exists. The lost commit aborted the
            // transaction that holds the copied reviewers (#115), so it is rolled back before the number is read again.
            await unitOfWork.RollbackAsync(cancellationToken);
            var concurrent = await versions.FindByNumberAsync(next.Course, next.Number, cancellationToken);
            if (concurrent.HasValue)
            {
                return Failure(CourseVersionFailures.NextVersionExists());
            }

            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(next.Id.Value);
    }

    /// <summary>
    /// Holds every reviewer whose approval the next version copies, until the commit, and answers those Editorial has
    /// erased (#115): an erasure that forgot their names after the source was loaded has tombstoned them first, so the
    /// copy leaves their names out, and one that comes later waits for this commit and forgets the copied names too. The
    /// reviewers are held in the order of their ids, so two drafts holding the same reviewers cannot deadlock.
    /// </summary>
    private async Task<IReadOnlySet<PersonId>> ErasedReviewersAsync(CourseVersion source, CancellationToken cancellationToken)
    {
        var reviewers = source.Questions
            .Select(question => question.ReviewedBy)
            .OfType<PersonId>()
            .Distinct()
            .OrderBy(reviewer => reviewer.Value);
        HashSet<PersonId> erasedReviewers = [];
        foreach (var reviewer in reviewers)
        {
            await erased.HoldAsync(reviewer, cancellationToken);
            if (await erased.IsErasedAsync(reviewer, cancellationToken))
            {
                erasedReviewers.Add(reviewer);
            }
        }

        return erasedReviewers;
    }
}
