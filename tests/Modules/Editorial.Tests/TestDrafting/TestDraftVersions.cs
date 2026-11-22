using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>Draft versions to draft a test for: text chapters with a blueprint, reached through the aggregate's own methods.</summary>
internal static class TestDraftVersions
{
    /// <summary>A draft whose blueprint asks for <paramref name="questions"/>, with a text chapter per title.</summary>
    public static CourseVersion WithTextChapters(int questions, params string[] titles)
    {
        var version = Draft("Kurs-Unikat 4711");
        foreach (var title in titles)
        {
            version.AddChapter(TextChapter(ChapterId.New(), title, $"Inhalt von {title}: was in der Apotheke dazu gilt.")).IsSuccess.ShouldBeTrue();
        }

        version.SetBlueprint(new TestBlueprint(QuestionCount.From(questions), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        return version;
    }

    /// <summary>
    /// A draft whose blueprint asks for <paramref name="questions"/>, with chapters in this order: text "Händehygiene",
    /// PDF "Merkblatt Hautschutz", text "Flächen", video "Lehrvideo Händedesinfektion".
    /// </summary>
    public static CourseVersion WithTextPdfAndVideoChapters(int questions)
    {
        var version = Draft("Kurs-Unikat 4711");
        version.AddChapter(TextChapter(ChapterId.New(), "Händehygiene", "Hände vor und nach der Rezeptur desinfizieren.")).IsSuccess.ShouldBeTrue();
        version.AddChapter(PdfChapter("Merkblatt Hautschutz")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "Flächen", "Rezepturtisch und Waage täglich desinfizieren.")).IsSuccess.ShouldBeTrue();
        version.AddChapter(VideoChapter("Lehrvideo Händedesinfektion")).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(questions), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        return version;
    }

    public static Chapter PdfChapter(string title) =>
        Chapter.OfPdf(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.pdf"));

    public static Chapter VideoChapter(string title) =>
        Chapter.OfVideo(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.mp4"), VideoDuration.From(90));

    public static IReadOnlyList<ChapterId> FileChapterIds(CourseVersion version) =>
        [.. version.Chapters.Where(chapter => chapter.Kind != ChapterKind.Text).Select(chapter => chapter.Id)];

    public static IReadOnlyList<ChapterId> TextChapterIds(CourseVersion version) =>
        [.. version.Chapters.Where(chapter => chapter.Kind == ChapterKind.Text).Select(chapter => chapter.Id)];
}
