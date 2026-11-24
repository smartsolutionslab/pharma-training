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
    /// video "Lehrvideo Händedesinfektion", PDF "Merkblatt Hautschutz", text "Flächen", PDF "Merkblatt Flächendesinfektion".
    /// The three file chapters' ids are handed out so that the version's order (video, PDF, PDF), their ascending id
    /// order (second PDF, first PDF, video) and their kind order (first PDF, second PDF, video) all differ: a drafter that
    /// sorts the chapters it left out by id or by kind does not name them in <paramref name="leftOutInOrder"/>.
    /// </summary>
    /// <param name="questions">The questions the blueprint asks for.</param>
    /// <param name="leftOutInOrder">The PDF and video chapters' ids in the version's order, written down as they were added.</param>
    public static CourseVersion WithTextPdfAndVideoChapters(int questions, out IReadOnlyList<ChapterId> leftOutInOrder)
    {
        ChapterId[] created = [ChapterId.New(), ChapterId.New(), ChapterId.New()];
        List<ChapterId> fileIds = [.. created.OrderBy(id => id.Value)];
        var video = fileIds[2];
        var firstPdf = fileIds[1];
        var secondPdf = fileIds[0];
        var version = Draft("Kurs-Unikat 4711");
        version.AddChapter(TextChapter(ChapterId.New(), "Händehygiene", "Hände vor und nach der Rezeptur desinfizieren.")).IsSuccess.ShouldBeTrue();
        version.AddChapter(VideoChapter(video, "Lehrvideo Händedesinfektion")).IsSuccess.ShouldBeTrue();
        version.AddChapter(PdfChapter(firstPdf, "Merkblatt Hautschutz")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "Flächen", "Rezepturtisch und Waage täglich desinfizieren.")).IsSuccess.ShouldBeTrue();
        version.AddChapter(PdfChapter(secondPdf, "Merkblatt Flächendesinfektion")).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(questions), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        leftOutInOrder = [video, firstPdf, secondPdf];
        return version;
    }

    /// <inheritdoc cref="WithTextPdfAndVideoChapters(int, out IReadOnlyList{ChapterId})"/>
    public static CourseVersion WithTextPdfAndVideoChapters(int questions) => WithTextPdfAndVideoChapters(questions, out _);

    public static Chapter PdfChapter(string title) => PdfChapter(ChapterId.New(), title);

    public static Chapter PdfChapter(ChapterId id, string title) =>
        Chapter.OfPdf(id, ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.pdf"), PdfTextExtraction.NoText, null);

    /// <summary>A PDF chapter whose text was extracted when it was recorded: whole, or cut at a limit.</summary>
    public static Chapter PdfWithText(ChapterId id, string title, string text, PdfTextExtraction extraction = PdfTextExtraction.Complete) =>
        Chapter.OfPdf(id, ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.pdf"), extraction, PdfText.From(text));

    /// <summary>A PDF chapter whose pages held no text, a scan.</summary>
    public static Chapter PdfWithoutText(ChapterId id, string title) => PdfChapter(id, title);

    /// <summary>
    /// A PDF chapter recorded before #157, as EF Core loads it from a row whose <c>pdf_text_extraction</c> is null: no
    /// method of the domain makes one, so the test clears the property the way the materializer leaves it.
    /// </summary>
    public static Chapter LegacyPdf(ChapterId id, string title)
    {
        var chapter = PdfChapter(id, title);
        typeof(Chapter).GetProperty(nameof(Chapter.Extraction)).ShouldNotBeNull().SetValue(chapter, null);
        chapter.Extraction.ShouldBeNull();
        return chapter;
    }

    /// <summary>
    /// A draft whose blueprint asks for <paramref name="questions"/>, with one chapter per letter of
    /// <paramref name="layout"/>, in its order: <c>T</c> text, <c>P</c> a PDF with its whole text, <c>R</c> a PDF whose
    /// text was cut at a limit, <c>N</c> a PDF without text, <c>L</c> a PDF recorded before #157, <c>V</c> a video.
    /// Each chapter's text says which it is ("Text 1 …", "PDF 2 …"), counted in the layout from 1.
    /// </summary>
    /// <param name="questions">The questions the blueprint asks for.</param>
    /// <param name="layout">The chapters' kinds in the version's order.</param>
    /// <param name="ids">The chapters' ids in the version's order.</param>
    public static CourseVersion WithChapters(int questions, string layout, out IReadOnlyList<ChapterId> ids)
    {
        var version = Draft("Kurs-Unikat 4711");
        List<ChapterId> added = [];
        for (var index = 0; index < layout.Length; index++)
        {
            var id = ChapterId.New();
            var position = index + 1;
            var chapter = layout[index] switch
            {
                'T' => TextChapter(id, $"Textkapitel {position}", TextOf(layout[index], position)),
                'P' => PdfWithText(id, $"PDF-Kapitel {position}", TextOf(layout[index], position)),
                'R' => PdfWithText(id, $"PDF-Kapitel {position}", TextOf(layout[index], position), PdfTextExtraction.Truncated),
                'N' => PdfWithoutText(id, $"Scan {position}"),
                'L' => LegacyPdf(id, $"Altes PDF {position}"),
                'V' => VideoChapter(id, $"Video {position}"),
                _ => throw new System.Diagnostics.UnreachableException(layout),
            };
            version.AddChapter(chapter).IsSuccess.ShouldBeTrue();
            added.Add(id);
        }

        version.SetBlueprint(new TestBlueprint(QuestionCount.From(questions), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        ids = added;
        return version;
    }

    /// <summary>The text <see cref="WithChapters"/> gives the chapter at <paramref name="position"/> of the kind.</summary>
    public static string TextOf(char kind, int position) => kind switch
    {
        'T' => $"Text {position}: Zytostatika werden getrennt von anderen Arzneimitteln gelagert.",
        _ => $"PDF {position}: Apotheke am Markt · Arbeitsanweisung\nZytostatika werden ge-\nkennzeichnet.\n\nSeite {position}",
    };

    public static Chapter VideoChapter(string title) => VideoChapter(ChapterId.New(), title);

    public static Chapter VideoChapter(ChapterId id, string title) =>
        Chapter.OfVideo(id, ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.mp4"), VideoDuration.From(90));

    public static IReadOnlyList<ChapterId> TextChapterIds(CourseVersion version) =>
        [.. version.Chapters.Where(chapter => chapter.Kind == ChapterKind.Text).Select(chapter => chapter.Id)];

    /// <summary>
    /// A version the drafter refuses before it asks the model, in the <paramref name="state"/> a test names: "not a draft",
    /// "no text chapters", "nothing to draft" or "too much content".
    /// </summary>
    public static CourseVersion Undraftable(string state) => state switch
    {
        "not a draft" => InReview(),
        "no text chapters" => Draft(),
        "nothing to draft" => Full(),
        "too much content" => TooLong(),
        _ => throw new System.Diagnostics.UnreachableException(state),
    };

    private static CourseVersion Full()
    {
        var version = WithTextChapters(1, "Händehygiene");
        var chapter = TextChapterIds(version)[0];
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        return version;
    }

    private static CourseVersion TooLong()
    {
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        return version;
    }
}
