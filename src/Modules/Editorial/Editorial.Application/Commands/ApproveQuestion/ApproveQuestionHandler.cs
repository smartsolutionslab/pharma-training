using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;

public sealed class ApproveQuestionHandler(ICourseVersionStore versions, IErasedPersonStore erased, IEditorialUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<ApproveQuestion, Result<Unit, ApiError>>
{
    // Not used yet: slice 3's red tests (#115) ask for the approver to be held and a tombstoned one stored without a name.
    private readonly IErasedPersonStore erased = erased;

    public async Task<Result<Unit, ApiError>> HandleAsync(ApproveQuestion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var approved = found.Value.ApproveQuestion(command.Question, command.Approver, command.ApproverName, Timestamp.From(time.GetUtcNow()));
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
