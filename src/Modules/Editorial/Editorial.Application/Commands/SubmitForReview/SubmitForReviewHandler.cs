using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SubmitForReview;

public sealed class SubmitForReviewHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<SubmitForReview, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(SubmitForReview command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var submitted = found.Value.SubmitForReview(command.Editor);
        if (submitted.IsFailure)
        {
            return Failure(CourseVersionFailures.From(submitted.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
