using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>Versions to review, in any state and with chapters of any kind, reached through the aggregate's own methods.</summary>
internal static class ContentReviewVersions
{
    /// <summary>A draft with a text chapter per title, each with a text of its own.</summary>
    public static CourseVersion WithTextChapters(params string[] titles)
    {
        var version = Draft("Kurs-Unikat 4711");
        foreach (var title in titles)
        {
            version.AddChapter(TextChapter(ChapterId.New(), title, $"Inhalt von {title}: was in der Apotheke dazu gilt.")).IsSuccess.ShouldBeTrue();
        }

        return version;
    }

    /// <summary>A version with one text chapter in <paramref name="status"/>, reached the way an editor reaches it.</summary>
    public static CourseVersion In(CourseVersionStatus status) => status switch
    {
        CourseVersionStatus.Draft => DraftWithChapter(out _),
        CourseVersionStatus.InReview => InReview(),
        CourseVersionStatus.Published => Published(),
        CourseVersionStatus.Superseded => Superseded(),
        CourseVersionStatus.Retired => Retired(),
        _ => throw new System.Diagnostics.UnreachableException(status.ToString()),
    };

    public static IReadOnlyList<ChapterId> TextChapterIds(CourseVersion version) =>
        [.. version.Chapters.Where(chapter => chapter.Kind == ChapterKind.Text).Select(chapter => chapter.Id)];

    public static Chapter PdfChapter(string title) =>
        Chapter.OfPdf(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.pdf"));

    public static Chapter VideoChapter(string title) =>
        Chapter.OfVideo(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.mp4"), VideoDuration.From(90));

    private static CourseVersion Superseded()
    {
        var version = Published();
        version.Supersede();
        return version;
    }

    private static CourseVersion Retired()
    {
        var version = Published();
        version.Retire(Author, Now).IsSuccess.ShouldBeTrue();
        return version;
    }
}
