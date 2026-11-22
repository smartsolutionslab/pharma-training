using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddFileChapter;

public sealed class AddFileChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IStoredBlobs blobs)
    : ICommandHandler<AddFileChapter, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(AddFileChapter command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        FileChapters.EnsureContentMatchesKind(command.Kind, command.Duration);

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var accepted = await ChapterUploads.AcceptAsync(blobs, command.File, command.Version, command.Kind, cancellationToken);
        if (accepted.IsFailure)
        {
            return Failure(accepted.Error);
        }

        var changed = found.Value.AddChapter(FileChapters.Of(ChapterId.New(), command.Title, command.Kind, command.File, command.Duration));
        if (changed.IsFailure)
        {
            return Failure(CourseVersionFailures.From(changed.Error));
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(changed.Value.Value);
    }
}
