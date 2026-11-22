using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Tests.Fakes;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing.ContentReviewerAskTests;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing.ReviewAnswers;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>
/// The whole review as the handler runs it: the request from the version, the model's answer, and the checked hints. A
/// version that cannot be reviewed never reaches the model; a malformed answer gives no hints at all.
/// </summary>
public class ContentReviewerReviewTests
{
    [Theory]
    [InlineData("no chapters", "editorial.no-text-chapters-to-review")]
    [InlineData("only a pdf and a video", "editorial.no-text-chapters-to-review")]
    [InlineData("too much content", "editorial.too-much-content-to-review")]
    public async Task Review_refuses_before_asking_the_model_when_the_version_cannot_be_reviewed(string state, string code)
    {
        var version = Unreviewable(state);
        using var model = new FakeChatClient(Sound(1));

        var result = await ContentReviewer.ReviewAsync(model, version, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Review_returns_the_hints_of_a_sound_answer()
    {
        var version = ContentReviewVersions.WithTextChapters("Händehygiene", "Flächen");
        var chapters = ContentReviewVersions.TextChapterIds(version);
        using var model = new FakeChatClient(Answer(Hint(2, "Gap", "Schritt 3 fehlt."), Hint(1, "Readability", "Der Satz ist zu lang.")));

        var result = await ContentReviewer.ReviewAsync(model, version, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Reviewed.ShouldBe(chapters);
        result.Value.Hints.ShouldBe(
        [
            new ContentHint(chapters[0], ContentHintKind.Readability, "Der Satz ist zu lang."),
            new ContentHint(chapters[1], ContentHintKind.Gap, "Schritt 3 fehlt."),
        ]);
        model.Requests.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("Alles gut.", nameof(ContentReviewDefect.Unreadable))]
    [InlineData("""{"hints":[{"chapter":3,"kind":"Gap","text":"Schritt 3 fehlt."}]}""", nameof(ContentReviewDefect.UnknownChapter))]
    [InlineData("""{"hints":[{"chapter":1,"kind":"Gap","text":"   "}]}""", nameof(ContentReviewDefect.InvalidHintText))]
    public async Task Review_reports_a_malformed_answer_and_returns_no_hints(string answer, string defect)
    {
        var version = ContentReviewVersions.WithTextChapters("Händehygiene", "Flächen");
        using var model = new FakeChatClient(answer);

        var result = await ContentReviewer.ReviewAsync(model, version, TestContext.Current.CancellationToken);

        Malformed(result, Enum.Parse<ContentReviewDefect>(defect));
    }

    private static CourseVersion Unreviewable(string state) => state switch
    {
        "no chapters" => Draft(),
        "only a pdf and a video" => FileChaptersOnly(),
        "too much content" => TooLong(),
        _ => throw new System.Diagnostics.UnreachableException(state),
    };

    private static CourseVersion FileChaptersOnly()
    {
        var version = Draft();
        version.AddChapter(ContentReviewVersions.PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        version.AddChapter(ContentReviewVersions.VideoChapter("Lehrvideo")).IsSuccess.ShouldBeTrue();
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
