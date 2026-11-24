using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddFileChapter;

public sealed class AddFileChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IStoredBlobs blobs, IPdfTextExtractor extractor)
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

        // The version is checked before the storage is asked: a version that takes no chapter, or a file another chapter
        // of it already holds, needs no look at a blob.
        var chapter = ChapterId.New();
        var accepts = found.Value.AcceptsFile(chapter, command.File);
        if (accepts.IsFailure)
        {
            return Failure(CourseVersionFailures.From(accepts.Error));
        }

        var accepted = await ChapterUploads.AcceptAsync(blobs, command.File, command.Version, command.Kind, cancellationToken);
        if (accepted.IsFailure)
        {
            return Failure(accepted.Error);
        }

        // A PDF's text is read now, from the file just accepted, and saved with the chapter (ADR 0008 D11).
        PdfChapterText? text = null;
        if (command.Kind == ChapterKind.Pdf)
        {
            var extracted = await PdfChapterTexts.ExtractAsync(blobs, extractor, accepted.Value, cancellationToken);
            if (extracted.IsFailure)
            {
                return Failure(extracted.Error);
            }

            text = extracted.Value;
        }

        var changed = found.Value.AddChapter(FileChapters.Of(chapter, command.Title, command.File, command.Duration, text));
        if (changed.IsFailure)
        {
            return Failure(CourseVersionFailures.From(changed.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(changed.Value.Value);
    }
}
