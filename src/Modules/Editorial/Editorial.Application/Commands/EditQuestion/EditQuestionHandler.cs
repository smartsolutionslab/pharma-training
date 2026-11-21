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

        // The version does not check the chapter of an edited question; the database would refuse it as a fault.
        var version = found.Value;
        if (version.Chapters.All(chapter => chapter.Id != command.Draft.Chapter))
        {
            return Failure(CourseVersionFailures.From(CourseVersionErrors.ChapterNotFound(version.Id, command.Draft.Chapter)));
        }

        var edited = version.EditQuestion(command.Question, command.Draft, AnswerOptionId.New);
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
