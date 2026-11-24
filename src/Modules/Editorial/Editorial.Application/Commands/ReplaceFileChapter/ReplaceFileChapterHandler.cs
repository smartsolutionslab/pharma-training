using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;

public sealed class ReplaceFileChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IStoredBlobs blobs, IPdfTextExtractor extractor)
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

        // The version and the chapter are checked before the storage is asked, in the order ReplaceChapter checks them:
        // a version that is no draft, then a chapter it does not hold.
        var accepts = found.Value.AcceptsUploads();
        if (accepts.IsFailure)
        {
            return Failure(CourseVersionFailures.From(accepts.Error));
        }

        if (found.Value.Chapters.SingleOrDefault(chapter => chapter.Id == command.Chapter) is not { } current)
        {
            return Failure(CourseVersionFailures.From(CourseVersionErrors.ChapterNotFound(command.Version, command.Chapter)));
        }

        // A chapter that keeps its file of the same kind needs no new upload: a chapter copied into this version names
        // its file under the version it was copied from, and that file was checked when it was first recorded.
        // It keeps its extraction too: a chapter recorded before extraction existed keeps having none to draft from.
        var keepsItsFile = current.Kind == command.Kind && current.File == command.File;
        var text = command.Kind == ChapterKind.Pdf && keepsItsFile
            ? new PdfChapterText(current.Extraction ?? PdfTextExtraction.NoText, current.ExtractedText)
            : null;
        if (!keepsItsFile)
        {
            // A file another chapter of the version holds is in use before the storage is asked about it.
            var free = found.Value.AcceptsFile(command.Chapter, command.File);
            if (free.IsFailure)
            {
                return Failure(CourseVersionFailures.From(free.Error));
            }

            var accepted = await ChapterUploads.AcceptAsync(blobs, command.File, command.Version, command.Kind, cancellationToken);
            if (accepted.IsFailure)
            {
                return Failure(accepted.Error);
            }

            // A new PDF's text is read now, from the file just accepted, and saved with the chapter (ADR 0008 D11).
            if (command.Kind == ChapterKind.Pdf)
            {
                var extracted = await PdfChapterTexts.ExtractAsync(blobs, extractor, accepted.Value, cancellationToken);
                if (extracted.IsFailure)
                {
                    return Failure(extracted.Error);
                }

                text = extracted.Value;
            }
        }

        var changed = found.Value.ReplaceChapter(FileChapters.Of(command.Chapter, command.Title, command.File, command.Duration, text));
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
