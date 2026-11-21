using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddTextChapter;

public sealed class AddTextChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<AddTextChapter, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(AddTextChapter command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var changed = found.Value.AddChapter(Chapter.OfText(ChapterId.New(), command.Title, command.Text));
        if (changed.IsFailure)
        {
            return Failure(CourseVersionFailures.From(changed.Error));
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Success(changed.Value.Value);
    }
}
