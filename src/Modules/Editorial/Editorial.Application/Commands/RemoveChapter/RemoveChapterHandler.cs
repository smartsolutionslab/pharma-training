using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveChapter;

public sealed class RemoveChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<RemoveChapter, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(RemoveChapter command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var changed = found.Value.RemoveChapter(command.Chapter);
        if (changed.IsFailure)
        {
            return Failure(CourseVersionFailures.From(changed.Error));
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Success(Unit.Value);
    }
}
