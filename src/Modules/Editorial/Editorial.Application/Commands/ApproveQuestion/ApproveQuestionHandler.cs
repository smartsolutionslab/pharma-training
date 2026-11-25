using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;

/// <summary>
/// Approves a question under the approver's id and display name; an approver Editorial has erased (#115) is recorded under
/// their id alone.
/// </summary>
public sealed class ApproveQuestionHandler(ICourseVersionStore versions, IErasedPersonStore erased, IEditorialUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<ApproveQuestion, Result<Unit, ApiError>>
{
    private readonly IErasedPersonStore erased = erased;

    public async Task<Result<Unit, ApiError>> HandleAsync(ApproveQuestion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        // Held until the commit, as the erasure holds them, so the name is either forgotten after this approval or not
        // stored at all: an erased approver's account and token can outlive their erasure (#115). Held after the load,
        // which reads in a snapshot transaction of its own; the hold begins the transaction the commit ends.
        await erased.HoldAsync(command.Approver, cancellationToken);
        var name = await erased.IsErasedAsync(command.Approver, cancellationToken) ? null : command.ApproverName;

        var approved = found.Value.ApproveQuestion(command.Question, command.Approver, name, Timestamp.From(time.GetUtcNow()));
        if (approved.IsFailure)
        {
            return Failure(CourseVersionFailures.From(approved.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
