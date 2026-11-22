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

    public static IReadOnlyList<ChapterId> TextChapterIds(CourseVersion version) =>
        [.. version.Chapters.Where(chapter => chapter.Kind == ChapterKind.Text).Select(chapter => chapter.Id)];
}
