using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Media;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

public static class LearningMediaMappings
{
    /// <summary>The read URL signed on a chapter's copy, with the content type of the chapter's kind.</summary>
    public static LearningMediaUrlDto ToDto(this BlobUrl url, ChapterKind kind)
    {
        Ensure.That(url).IsNotNull();

        return new LearningMediaUrlDto(url.Url, url.ExpiresAt, ChapterMedia.ContentTypeOf(kind), ToKind(kind));
    }

    private static LearningChapterKind ToKind(ChapterKind kind) => kind switch
    {
        ChapterKind.Text => LearningChapterKind.Text,
        ChapterKind.Pdf => LearningChapterKind.Pdf,
        ChapterKind.Video => LearningChapterKind.Video,
        _ => throw new InvalidOperationException($"Chapter kind {kind} has no counterpart on the wire."),
    };
}
