using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;

public sealed class ApproveQuestionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<ApproveQuestion, Result<Unit, ApiError>>
{
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

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
