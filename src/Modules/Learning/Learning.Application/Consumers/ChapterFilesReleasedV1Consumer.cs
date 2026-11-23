using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Media;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Copies each file Editorial released into Learning's own container, server side, under the name
/// <see cref="ChapterMedia.NameFor"/> derives from the version and the chapter (#41, docs/adr/0002). The derived name is
/// the deduplication: a file already there counts as copied, so a retry, a second delivery or a lost race succeed
/// without copying again, and no row is written. A copy that fails, an expired source first, throws, so the message is
/// retried and then dead-lettered. It does not ask whether Learning holds the version: this event may arrive before
/// <c>CourseVersionPublishedV1</c>.
/// </summary>
public sealed class ChapterFilesReleasedV1Consumer(IBlobCopies copies)
{
    public async Task HandleAsync(ChapterFilesReleasedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, before anything is copied, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.Files, nameof(message.Files)).IsNotNull();
        Ensure.That(message.Files.Count, nameof(message.Files)).AtLeast(1);
        HashSet<Guid> chapterIds = [];
        foreach (var file in message.Files)
        {
            Ensure.That(file, nameof(message.Files)).IsNotNull();
            Ensure.That(file.ChapterId, nameof(file.ChapterId)).IsNotEmpty();
            Ensure.That(file.ChapterId.ToString(), nameof(file.ChapterId)).Satisfies(_ => chapterIds.Add(file.ChapterId), "A chapter id may appear once in a release.");
            Ensure.That((int)file.Kind, nameof(file.Kind)).Satisfies(_ => file.Kind is ChapterKindV1.Pdf or ChapterKindV1.Video, "Only a PDF or video chapter has a file.");
            Ensure.That(file.ReadUrl, nameof(file.ReadUrl)).IsNotNull();
        }

        var version = CourseVersionId.From(message.CourseVersionId);
        foreach (var file in message.Files)
        {
            var target = ChapterMedia.NameFor(version, ChapterId.From(file.ChapterId), KindOf(file.Kind));
            await copies.CopyFromAsync(new BlobUrl(file.ReadUrl, file.ExpiresAt), target, cancellationToken);
        }
    }

    private static ChapterKind KindOf(ChapterKindV1 kind) => kind switch
    {
        ChapterKindV1.Pdf => ChapterKind.Pdf,
        ChapterKindV1.Video => ChapterKind.Video,
        _ => throw new InvalidOperationException($"Chapter kind {kind} has no file."),
    };
}
