using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A published version is announced twice (story E6): to every module as the course with its chapters and without a
/// single question, and to Assessment alone as its test, with every question and its solutions.
/// </summary>
public class IntegrationEventMappingsTests
{
    private static readonly string[] SolutionProperties = ["questions", "options", "answerOptions", "isCorrect", "correct", "correctness"];

    private static readonly string[] QuestionAndAnswerTexts =
    [
        "Welche Temperatur gilt im Kühlschrank?",
        "Welche Temperatur",
        "2 bis 8 °C",
        "12 bis 15 °C",
        "Was gehört zur Kühlkette?",
        "Lieferung",
        "Lagerung",
        "Verkaufsraum",
    ];

    [Fact]
    public void A_published_version_is_announced_with_its_chapters_in_order_and_their_kinds_durations_and_validity()
    {
        var version = PublishedWithEveryKind(out var text, out var pdf, out var video);

        var announced = version.ToCourseVersionPublishedV1();

        announced.CourseId.ShouldBe(version.Course.Value);
        announced.CourseVersionId.ShouldBe(version.Id.Value);
        announced.VersionNumber.ShouldBe(1);
        announced.Title.ShouldBe("Kühlkette 2027");
        announced.ValidityMonths.ShouldBe(24);
        announced.Chapters.Select(chapter => chapter.ChapterId).ShouldBe([text.Value, pdf.Value, video.Value]);
        announced.Chapters.Select(chapter => chapter.Position).ShouldBe([1, 2, 3]);
        announced.Chapters.Select(chapter => chapter.Title).ShouldBe(["Grundlagen", "Merkblatt", "Praxis"]);
        announced.Chapters.Select(chapter => chapter.Kind).ShouldBe([ChapterKindV1.Text, ChapterKindV1.Pdf, ChapterKindV1.Video]);
        announced.Chapters.Select(chapter => chapter.VideoDurationSeconds).ShouldBe([null, null, 754]);
        announced.Chapters.Select(chapter => chapter.Text).ShouldBe(["# Grundlagen", null, null]);
        announced.Chapters.Select(chapter => chapter.File).ShouldBe([null, "courses/kuehlkette/merkblatt.pdf", "courses/kuehlkette/praxis.mp4"]);
    }

    /// <summary>The issue's first "done when": the questions with their solutions go to Assessment only, never with the course.</summary>
    [Fact]
    public void The_course_version_event_carries_no_question_and_no_answer_correctness()
    {
        var version = PublishedWithEveryKind(out _, out _, out _);

        // An unpublished event's Metadata throws when read; this fills a test copy only, as the unit of work would.
        var announced = version.ToCourseVersionPublishedV1() with
        {
            Metadata = new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()),
        };
        var json = JsonSerializer.Serialize(announced, JsonSerializerOptions.Web);

        var (names, values) = Read(json);
        names.Where(name => SolutionProperties.Contains(name, StringComparer.OrdinalIgnoreCase)).ShouldBeEmpty(json);

        // The values as read back, not the raw JSON, whose encoder escapes "°" and "ü".
        foreach (var said in QuestionAndAnswerTexts)
        {
            values.ShouldNotContain(value => value.Contains(said, StringComparison.Ordinal), json);
        }
    }

    [Fact]
    public void A_published_version_is_announced_to_assessment_with_every_question_its_solutions_and_three_attempts()
    {
        var version = PublishedWithEveryKind(out var text, out _, out var video);
        var stored = version.Questions;

        var announced = version.ToTestDefinitionPublishedV1();

        announced.CourseId.ShouldBe(version.Course.Value);
        announced.CourseVersionId.ShouldBe(version.Id.Value);
        announced.PassingScorePercent.ShouldBe(70);
        announced.MaxAttempts.ShouldBe(3);
        announced.Questions.Select(question => question.QuestionId).ShouldBe([stored[0].Id.Value, stored[1].Id.Value]);
        announced.Questions.Select(question => question.ChapterId).ShouldBe([text.Value, video.Value]);
        announced.Questions.Select(question => question.Type).ShouldBe([QuestionTypeV1.SingleChoice, QuestionTypeV1.MultipleChoice]);
        announced.Questions.Select(question => question.Text).ShouldBe(["Welche Temperatur gilt im Kühlschrank?", "Was gehört zur Kühlkette?"]);

        var single = announced.Questions[0].Options;
        single.Select(option => option.AnswerOptionId).ShouldBe([.. stored[0].Options.Select(option => option.Id.Value)]);
        single.Select(option => option.Text).ShouldBe(["2 bis 8 °C", "12 bis 15 °C"]);
        single.Select(option => option.IsCorrect).ShouldBe([true, false]);

        var multiple = announced.Questions[1].Options;
        multiple.Select(option => option.AnswerOptionId).ShouldBe([.. stored[1].Options.Select(option => option.Id.Value)]);
        multiple.Select(option => option.Text).ShouldBe(["Lieferung", "Lagerung", "Verkaufsraum"]);
        multiple.Select(option => option.IsCorrect).ShouldBe([true, true, false]);
    }

    /// <summary>
    /// #41: Learning signs only for its own container, so Editorial releases each PDF and video chapter's file with a read
    /// URL it signed on its own blob, which Learning copies from; in chapter order, each with the URL's expiry.
    /// </summary>
    [Fact]
    public void ChapterFilesReleasedV1_lists_each_pdf_and_video_chapter_with_its_read_url_and_expiry()
    {
        var version = PublishedWithEveryKind(out _, out var pdf, out var video);
        var pdfUrl = new BlobUrl(new Uri("https://blobs.example/editorial-blobs/courses/kuehlkette/merkblatt.pdf?sp=r&sig=pdf"), new DateTimeOffset(2027, 3, 1, 9, 15, 0, TimeSpan.Zero));
        var videoUrl = new BlobUrl(new Uri("https://blobs.example/editorial-blobs/courses/kuehlkette/praxis.mp4?sp=r&sig=video"), new DateTimeOffset(2027, 3, 1, 9, 14, 59, TimeSpan.Zero));
        Dictionary<ChapterId, BlobUrl> readUrls = new() { [video] = videoUrl, [pdf] = pdfUrl };

        var released = version.ToChapterFilesReleasedV1(readUrls);

        released.CourseVersionId.ShouldBe(version.Id.Value);
        released.Files.ShouldBe(
        [
            new ReleasedChapterFileV1(pdf.Value, ChapterKindV1.Pdf, pdfUrl.Url, pdfUrl.ExpiresAt),
            new ReleasedChapterFileV1(video.Value, ChapterKindV1.Video, videoUrl.Url, videoUrl.ExpiresAt),
        ]);
    }

    [Fact]
    public void ChapterFilesReleasedV1_leaves_out_text_chapters()
    {
        var version = PublishedWithEveryKind(out var text, out var pdf, out var video);
        var expiresAt = new DateTimeOffset(2027, 3, 1, 9, 15, 0, TimeSpan.Zero);
        Dictionary<ChapterId, BlobUrl> readUrls = new()
        {
            [pdf] = new BlobUrl(new Uri("https://blobs.example/editorial-blobs/merkblatt.pdf?sig=pdf"), expiresAt),
            [video] = new BlobUrl(new Uri("https://blobs.example/editorial-blobs/praxis.mp4?sig=video"), expiresAt),
        };

        var released = version.ToChapterFilesReleasedV1(readUrls);

        released.Files.Select(file => file.ChapterId).ShouldBe([pdf.Value, video.Value]);
        released.Files.ShouldNotContain(file => file.ChapterId == text.Value);
        released.Files.Select(file => file.Kind).ShouldNotContain(ChapterKindV1.Text);
    }

    /// <summary>A released file Learning could not copy is a bug of the publish, not a message to send without it.</summary>
    [Fact]
    public void A_file_chapter_without_a_signed_url_is_refused()
    {
        var version = PublishedWithEveryKind(out _, out var pdf, out _);
        Dictionary<ChapterId, BlobUrl> onlyThePdf = new()
        {
            [pdf] = new BlobUrl(new Uri("https://blobs.example/editorial-blobs/merkblatt.pdf?sig=pdf"), new DateTimeOffset(2027, 3, 1, 9, 15, 0, TimeSpan.Zero)),
        };

        Should.Throw<GuardException>(() => version.ToChapterFilesReleasedV1(onlyThePdf));
    }

    /// <summary>docs/domain/README.md: "höchstens 3 Versuche", later perhaps configurable; fixed for MVP 1.</summary>
    [Fact]
    public void The_maximum_of_attempts_is_three() =>
        EditorialIntegrationEventMappings.MaxAttempts.ShouldBe(3);

    /// <summary>
    /// A published first version with a text, a PDF and a video chapter, in that order, and two approved questions, as
    /// many as its blueprint asks: a single choice on the text chapter and a multiple choice on the video.
    /// </summary>
    private static CourseVersion PublishedWithEveryKind(out ChapterId text, out ChapterId pdf, out ChapterId video)
    {
        var version = CourseVersion.DraftFirst(CourseVersionId.New(), CourseId.New(), CourseTitle.From("Kühlkette 2027"), Author);
        text = ChapterId.New();
        pdf = ChapterId.New();
        video = ChapterId.New();
        version.AddChapter(Chapter.OfText(text, ChapterTitle.From("Grundlagen"), MarkdownText.From("# Grundlagen"))).IsSuccess.ShouldBeTrue();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Merkblatt"), BlobReference.From("courses/kuehlkette/merkblatt.pdf"))).IsSuccess.ShouldBeTrue();
        version.AddChapter(Chapter.OfVideo(video, ChapterTitle.From("Praxis"), BlobReference.From("courses/kuehlkette/praxis.mp4"), VideoDuration.From(754))).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(2), ScorePercent.From(70), RecurrenceInterval.From(24))).IsSuccess.ShouldBeTrue();

        var temperature = version.AddQuestion(
            QuestionId.New(),
            Drafted(text, "Welche Temperatur gilt im Kühlschrank?", QuestionType.SingleChoice, ("2 bis 8 °C", true), ("12 bis 15 °C", false)),
            AnswerOptionId.New).Value;
        var chain = version.AddQuestion(
            QuestionId.New(),
            Drafted(video, "Was gehört zur Kühlkette?", QuestionType.MultipleChoice, ("Lieferung", true), ("Lagerung", true), ("Verkaufsraum", false)),
            AnswerOptionId.New).Value;
        version.ApproveQuestion(temperature, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(chain, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        return version;
    }

    /// <summary>Every property name and every string value anywhere in the JSON.</summary>
    private static (List<string> Names, List<string> Values) Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        List<string> names = [];
        List<string> values = [];
        Collect(document.RootElement, names, values);
        return (names, values);
    }

    private static void Collect(JsonElement element, List<string> names, List<string> values)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                names.Add(property.Name);
                Collect(property.Value, names, values);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, names, values);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            values.Add(element.GetString() ?? string.Empty);
        }
    }
}
