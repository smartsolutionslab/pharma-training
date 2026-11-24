using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.EditQuestion;

public sealed class EditQuestionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<EditQuestion, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(EditQuestion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        // The version refuses a chapter it does not hold, after checking that it is a draft and holds the question.
        var edited = found.Value.EditQuestion(command.Question, command.Draft, command.Editor, AnswerOptionId.New);
        if (edited.IsFailure)
        {
            return Failure(CourseVersionFailures.From(edited.Error));
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
