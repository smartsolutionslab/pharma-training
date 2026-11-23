using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.DraftNextVersion;

/// <summary>
/// Drafts the next version of a published one, numbered one higher. A version has one successor at most: a second is
/// refused while the first exists, so a course has at most one unpublished version at any time, and two first
/// publishes of one course cannot race. Two drafts that race past the check meet at the unique (course, number)
/// index, which the host answers with 409 <c>already-exists</c>; that violation is never caught here.
/// </summary>
public sealed class DraftNextVersionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<DraftNextVersion, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(DraftNextVersion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var source = found.Value;
        var drafted = source.DraftNext(CourseVersionId.New(), command.Editor, ChapterId.New, QuestionId.New, AnswerOptionId.New);
        if (drafted.IsFailure)
        {
            return Failure(CourseVersionFailures.From(drafted.Error));
        }

        var next = drafted.Value;
        var existing = await versions.FindByNumberAsync(next.Course, next.Number, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(CourseVersionFailures.NextVersionExists(source.Id.Value, existing.Value.Value, next.Number.Value));
        }

        versions.Add(next);
        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            // The commit lost (a deadlock or a serialization failure). If another editor drafted this number meanwhile,
            // say so; a unique violation never reaches here, it answers 409 already-exists.
            var concurrent = await versions.FindByNumberAsync(next.Course, next.Number, cancellationToken);
            if (concurrent.HasValue)
            {
                return Failure(CourseVersionFailures.NextVersionExists(source.Id.Value, concurrent.Value.Value, next.Number.Value));
            }

            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(next.Id.Value);
    }
}
