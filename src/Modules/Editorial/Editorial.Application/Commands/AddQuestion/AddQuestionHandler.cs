using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddQuestion;

public sealed class AddQuestionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<AddQuestion, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(AddQuestion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        // The version refuses a question about a chapter it does not hold, after checking that it is a draft.
        var added = found.Value.AddQuestion(QuestionId.New(), command.Draft, AnswerOptionId.New);
        if (added.IsFailure)
        {
            return Failure(CourseVersionFailures.From(added.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(added.Value.Value);
    }
}
