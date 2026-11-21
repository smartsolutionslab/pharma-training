using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Consumers;

/// <summary>
/// Stores a course version Editorial published, once. A published version never changes, so a version already stored
/// means its event was handled, whatever the event's identifier: a second delivery changes nothing.
/// </summary>
public sealed class CourseVersionPublishedV1Consumer(ICourseVersionStore versions, ICatalogUnitOfWork unitOfWork)
{
    private const string NoNul = "must not contain a NUL character.";

    public async Task HandleAsync(CourseVersionPublishedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseId).IsNotEmpty();
        Ensure.That(message.CourseVersionId).IsNotEmpty();
        Ensure.That(message.Chapters).IsNotNull();
        EnsureNoNul(message.Title, nameof(message.Title));
        foreach (var chapter in message.Chapters)
        {
            Ensure.That(chapter).IsNotNull();
            EnsureNoNul(chapter.Title, nameof(chapter.Title));
            EnsureNoNul(chapter.Text, nameof(chapter.Text));
            EnsureNoNul(chapter.File, nameof(chapter.File));
        }

        if (await versions.ExistsAsync(message.CourseVersionId, cancellationToken))
        {
            return;
        }

        versions.Add(new CourseVersion
        {
            Id = message.CourseVersionId,
            CourseId = message.CourseId,
            VersionNumber = message.VersionNumber,
            Title = message.Title,
            ValidityMonths = message.ValidityMonths,
            Chapters = [.. message.Chapters.Select(chapter => ToChapter(message.CourseVersionId, chapter))],
        });
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private static void EnsureNoNul(string? value, string name) =>
        Ensure.That(value ?? string.Empty, name).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), NoNul);

    private static CourseChapter ToChapter(Guid courseVersionId, CourseChapterV1 chapter) => new()
    {
        CourseVersionId = courseVersionId,
        ChapterId = chapter.ChapterId,
        Position = chapter.Position,
        Title = chapter.Title,
        Kind = ToKind(chapter.Kind),
        VideoDurationSeconds = chapter.VideoDurationSeconds,
        Text = chapter.Text,
        File = chapter.File,
    };

    private static ChapterKind ToKind(ChapterKindV1 kind) => kind switch
    {
        ChapterKindV1.Text => ChapterKind.Text,
        ChapterKindV1.Pdf => ChapterKind.Pdf,
        ChapterKindV1.Video => ChapterKind.Video,
        _ => throw new InvalidOperationException($"Chapter kind {kind} has no counterpart in the catalog."),
    };
}
