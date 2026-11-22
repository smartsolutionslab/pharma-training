using System.Net;
using Microsoft.Extensions.AI;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ReviewContent;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Tests.Fakes;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing.ReviewAnswers;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>
/// <c>ReviewContent</c> through its handler: the stored version's hints as DTOs, by chapter id, kind and text. The
/// version is read and never changed; one that cannot be reviewed, or is not there, never reaches the model; a model
/// that fails is not caught.
/// </summary>
public class ReviewContentTests
{
    [Fact]
    public async Task A_sound_answer_returns_the_hints_with_chapter_ids_kinds_and_texts()
    {
        var store = new InMemoryCourseVersionStore();
        var version = ContentReviewVersions.WithTextChapters("Händehygiene", "Flächen");
        version.AddChapter(ContentReviewVersions.PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);
        var chapters = ContentReviewVersions.TextChapterIds(version);
        using var model = new FakeChatClient(Answer(
            Hint(2, "Contradiction", " „täglich“ widerspricht Kapitel „Händehygiene“. "),
            Hint(1, "Gap", "Schritt 3 fehlt."),
            Hint(1, "Readability", "Der Satz ist zu lang.")));

        var result = await new ReviewContentHandler(store, model).HandleAsync(new ReviewContent(version.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ReviewedChapterIds.ShouldBe([chapters[0].Value, chapters[1].Value]);
        result.Value.Hints.ShouldBe(
        [
            new EditorialContentHintDto(chapters[0].Value, EditorialContentHintKind.Gap, "Schritt 3 fehlt."),
            new EditorialContentHintDto(chapters[0].Value, EditorialContentHintKind.Readability, "Der Satz ist zu lang."),
            new EditorialContentHintDto(chapters[1].Value, EditorialContentHintKind.Contradiction, "„täglich“ widerspricht Kapitel „Händehygiene“."),
        ]);
        model.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_version_is_unchanged_by_a_review()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out _);
        store.Stored.Add(version);
        var before = Snapshot.Of(version);
        IReadOnlyList<object> eventsBefore = [.. version.DomainEvents];
        using var model = new FakeChatClient(Sound(1));

        var result = await new ReviewContentHandler(store, model).HandleAsync(new ReviewContent(version.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        Snapshot.Of(version).ShouldBe(before);
        version.DomainEvents.Count.ShouldBe(eventsBefore.Count);
        for (var index = 0; index < eventsBefore.Count; index++)
        {
            version.DomainEvents[index].ShouldBeSameAs(eventsBefore[index]);
        }

        store.Commits.ShouldBe(0);
        store.Stored.ShouldHaveSingleItem().ShouldBeSameAs(version);
    }

    [Fact]
    public async Task An_empty_or_unknown_id_is_not_found_and_the_model_is_not_asked()
    {
        using var model = new FakeChatClient(Sound(1));
        var handler = new ReviewContentHandler(new InMemoryCourseVersionStore(), model);

        var unknown = await handler.HandleAsync(new ReviewContent(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        var empty = await handler.HandleAsync(new ReviewContent(Guid.Empty), TestContext.Current.CancellationToken);

        foreach (var result in new[] { unknown, empty })
        {
            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe("editorial.course-version-not-found");
            result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        }

        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_without_text_chapters_is_refused_and_the_model_is_not_asked()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Draft();
        version.AddChapter(ContentReviewVersions.PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(1));

        var result = await new ReviewContentHandler(store, model).HandleAsync(new ReviewContent(version.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.no-text-chapters-to-review");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_malformed_answer_answers_502_with_the_ai_review_malformed_code()
    {
        var store = new InMemoryCourseVersionStore();
        var version = ContentReviewVersions.WithTextChapters("Händehygiene");
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(2));

        var result = await new ReviewContentHandler(store, model).HandleAsync(new ReviewContent(version.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.ai-review-malformed");
        result.Error.Status.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Theory]
    [InlineData("Gap, Readability")]
    [InlineData("Contradiction, Readability")]
    public async Task Kinds_joined_by_a_comma_answer_502_not_an_exception(string kind)
    {
        var store = new InMemoryCourseVersionStore();
        var version = ContentReviewVersions.WithTextChapters("Händehygiene");
        store.Stored.Add(version);
        using var model = new FakeChatClient(Answer(Hint(1, kind, "Schritt 3 fehlt.")));

        var result = await new ReviewContentHandler(store, model).HandleAsync(new ReviewContent(version.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.ai-review-malformed");
        result.Error.Status.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task An_exception_from_the_model_is_not_caught()
    {
        var store = new InMemoryCourseVersionStore();
        var version = ContentReviewVersions.WithTextChapters("Händehygiene");
        store.Stored.Add(version);
        var model = Substitute.For<IChatClient>();
        model.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("The provider answered 500."));
        var handler = new ReviewContentHandler(store, model);

        await Should.ThrowAsync<HttpRequestException>(() => handler.HandleAsync(new ReviewContent(version.Id.Value), TestContext.Current.CancellationToken));
    }

    /// <summary>Everything a review could change on a version, as values that compare equal when nothing changed.</summary>
    private sealed record Snapshot(string State)
    {
        public static Snapshot Of(CourseVersion version)
        {
            var chapters = version.Chapters.Select(chapter => $"{chapter.Id.Value}|{chapter.Kind}|{chapter.Title.Value}|{chapter.Text?.Value}|{chapter.File?.Value}|{chapter.Duration?.Value}");
            var questions = version.Questions.Select(question =>
                $"{question.Id.Value}|{question.Chapter.Value}|{question.Type}|{question.Text.Value}|{question.Review}|{question.ReviewedBy?.Value}|{question.ReviewedAt?.Value}|{question.Provenance}|"
                + string.Join(",", question.Options.Select(option => $"{option.Id.Value}:{option.Text.Value}:{option.Correctness}")));
            return new Snapshot(string.Join(
                "\n",
                [
                    $"{version.Id.Value}|{version.Number.Value}|{version.Title.Value}|{version.Status}",
                    $"{version.Blueprint.Questions.Value}|{version.Blueprint.PassingScore.Value}|{version.Blueprint.Validity}",
                    $"{version.CreatedBy.Value}|{version.SubmittedBy?.Value}|{version.PublishedBy?.Value}|{version.PublishedAt?.Value}",
                    .. chapters,
                    .. questions,
                ]));
        }
    }
}
