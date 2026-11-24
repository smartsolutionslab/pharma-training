using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceTextChapter;

public sealed class ReplaceTextChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<ReplaceTextChapter, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ReplaceTextChapter command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var changed = found.Value.ReplaceChapter(Chapter.OfText(command.Chapter, command.Title, command.Text), command.Editor);
        if (changed.IsFailure)
        {
            return Failure(CourseVersionFailures.From(changed.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
