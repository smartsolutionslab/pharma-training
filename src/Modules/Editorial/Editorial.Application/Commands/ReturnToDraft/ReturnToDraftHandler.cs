using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;

public sealed class ReturnToDraftHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<ReturnToDraft, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ReturnToDraft command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var returned = found.Value.ReturnToDraft(command.Editor, command.EditorName, command.Reason, Timestamp.From(time.GetUtcNow()));
        if (returned.IsFailure)
        {
            return Failure(CourseVersionFailures.From(returned.Error));
        }

        // The record is saved in the version's own transaction, under its row version: a reaction would run after the
        // commit and is never retried, so the audit trail could lose a return (ADR 0013).
        versions.AddReturn(returned.Value);

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
