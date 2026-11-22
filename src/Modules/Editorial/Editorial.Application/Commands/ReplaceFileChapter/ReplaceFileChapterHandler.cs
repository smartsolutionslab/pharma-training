using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;

public sealed class ReplaceFileChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IStoredBlobs blobs)
    : ICommandHandler<ReplaceFileChapter, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ReplaceFileChapter command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        FileChapters.EnsureContentMatchesKind(command.Kind, command.Duration);

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        // A chapter that keeps its file of the same kind needs no new upload: a chapter copied into this version names
        // its file under the version it was copied from, and that file was checked when it was first recorded.
        var keepsItsFile = found.Value.Chapters.Any(chapter => chapter.Id == command.Chapter && chapter.Kind == command.Kind && chapter.File == command.File);
        if (!keepsItsFile)
        {
            var accepted = await ChapterUploads.AcceptAsync(blobs, command.File, command.Version, command.Kind, cancellationToken);
            if (accepted.IsFailure)
            {
                return Failure(accepted.Error);
            }
        }

        var changed = found.Value.ReplaceChapter(FileChapters.Of(command.Chapter, command.Title, command.Kind, command.File, command.Duration));
        if (changed.IsFailure)
        {
            return Failure(CourseVersionFailures.From(changed.Error));
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
