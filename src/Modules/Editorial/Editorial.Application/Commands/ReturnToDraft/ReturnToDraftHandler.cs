using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;

/// <summary>
/// Returns a version to draft and stores the record of the return under the editor's id and display name; an editor
/// Editorial has erased (#115) is recorded under their id alone, with the reason.
/// </summary>
public sealed class ReturnToDraftHandler(ICourseVersionStore versions, IErasedPersonStore erased, IEditorialUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<ReturnToDraft, Result<Unit, ApiError>>
{
    private readonly IErasedPersonStore erased = erased;

    public async Task<Result<Unit, ApiError>> HandleAsync(ReturnToDraft command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        // Held until the commit, as the erasure holds them, so the name is either forgotten after this return or not
        // stored at all: an erased editor's account and token can outlive their erasure (#115).
        await erased.HoldAsync(command.Editor, cancellationToken);
        var name = await erased.IsErasedAsync(command.Editor, cancellationToken) ? null : command.EditorName;

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var returned = found.Value.ReturnToDraft(command.Editor, name, command.Reason, Timestamp.From(time.GetUtcNow()));
        if (returned.IsFailure)
        {
            return Failure(CourseVersionFailures.From(returned.Error));
        }

        // The record is saved in the version's own transaction, under its row version: a reaction would run after the
        // commit and is never retried, so the audit trail could lose a return (ADR 0013).
        versions.AddReturn(returned.Value);

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
