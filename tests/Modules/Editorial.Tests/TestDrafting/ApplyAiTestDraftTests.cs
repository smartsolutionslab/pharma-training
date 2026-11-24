using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using System.Net;
using Microsoft.Extensions.AI;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApplyAiTestDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Tests.Fakes;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting.DraftAnswers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>
/// The use case that drafts a version's test with the model and applies it (#22): one tracked load, the model asked
/// about that version, the draft applied to it and saved, and the ids of the drafted questions and of the chapters
/// left out answered. A version that cannot be drafted never reaches the model, and a failure saves nothing. No test
/// calls a real model.
/// </summary>
public class ApplyAiTestDraftTests
{
    private readonly InMemoryCourseVersionStore store = new();

    [Fact]
    public async Task Applying_a_draft_keeps_the_approved_and_hand_written_questions_and_replaces_the_unreviewed_ai_drafted_ones()
    {
        var version = TestDraftVersions.WithTextChapters(4, "Händehygiene", "Flächen");
        var chapter = TestDraftVersions.TextChapterIds(version)[0];
        var approved = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(approved, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        var earlierAi = QuestionId.New();
        version.ApplyAiTestDraft([SingleChoice(chapter)], () => earlierAi, AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        var handWritten = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.Questions.Select(question => question.Id).ShouldBe([approved, earlierAi, handWritten]);
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(1, 2));

        var result = await HandleAsync(model, version.Id);

        result.IsSuccess.ShouldBeTrue();
        version.Questions.Count.ShouldBe(4);
        var kept = version.Questions[0];
        kept.Id.ShouldBe(approved);
        kept.Review.ShouldBe(ReviewState.Approved);
        kept.ReviewedBy.ShouldBe(Author);
        version.Questions[1].Id.ShouldBe(handWritten);
        version.Questions[1].Origin.ShouldBe(QuestionOrigin.HandWritten);
        var drafted = version.Questions.Skip(2).ToList();
        drafted.Count.ShouldBe(2);
        drafted.ShouldAllBe(question => question.Provenance == Provenance.AiDrafted && question.Origin == QuestionOrigin.AiDraft && question.Review == ReviewState.Unreviewed);
        version.Questions.ShouldNotContain(question => question.Id == earlierAi);
        result.Value.DraftedQuestionIds.ShouldBe([.. drafted.Select(question => question.Id.Value)]);
        result.Value.LeftOutChapterIds.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    /// <summary>The issue's case (#245): changing a setting resets every approval, and the draft that follows keeps every copied and hand-written question.</summary>
    [Fact]
    public async Task After_a_settings_change_a_draft_keeps_every_copied_and_hand_written_question_and_asks_only_for_the_rest()
    {
        var next = Published().DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        var chapter = next.Chapters.ShouldHaveSingleItem().Id;
        var copied = next.Questions.ShouldHaveSingleItem().Id;
        var handWritten = next.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        next.SetBlueprint(new TestBlueprint(QuestionCount.From(4), ScorePercent.From(90), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        next.Questions.ShouldAllBe(question => question.Review == ReviewState.Unreviewed);
        store.Stored.Add(next);
        using var model = new FakeChatClient(Sound(1, 1));

        var result = await HandleAsync(model, next.Id);

        result.IsSuccess.ShouldBeTrue();
        var user = model.Requests.ShouldHaveSingleItem().Where(message => message.Role == ChatRole.User).ShouldHaveSingleItem().Text;
        user.ShouldContain("Draft exactly 2 questions.");
        next.Questions.Select(question => question.Id).ShouldBe([copied, handWritten, .. result.Value.DraftedQuestionIds.Select(QuestionId.From)]);
        next.Questions.Select(question => question.Origin).ShouldBe([QuestionOrigin.Copied, QuestionOrigin.HandWritten, QuestionOrigin.AiDraft, QuestionOrigin.AiDraft]);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_response_names_the_pdf_chapters_without_text_and_video_chapters_left_out_in_the_versions_order()
    {
        var version = TestDraftVersions.WithTextPdfAndVideoChapters(2, out var leftOut);
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(1, 2));

        var result = await HandleAsync(model, version.Id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LeftOutChapterIds.ShouldBe([.. leftOut.Select(chapter => chapter.Value)]);
        result.Value.DraftedQuestionIds.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_version_of_pdf_chapters_only_is_drafted_and_applied_with_questions_on_its_pdf_chapters()
    {
        var version = TestDraftVersions.WithChapters(4, "PP", out var ids);
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(1, 2, 2, 1));

        var result = await HandleAsync(model, version.Id);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        version.Questions.Select(question => question.Chapter).ShouldBe([ids[0], ids[1], ids[1], ids[0]]);
        version.Questions.ShouldAllBe(question => question.Provenance == Provenance.AiDrafted && question.Review == ReviewState.Unreviewed);
        result.Value.DraftedQuestionIds.ShouldBe([.. version.Questions.Select(question => question.Id.Value)]);
        result.Value.LeftOutChapterIds.ShouldBeEmpty();
        result.Value.TruncatedChapterIds.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_response_names_the_left_out_and_the_cut_off_chapters_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(4, "RVTLPN", out var ids);
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(1, 2, 3, 1));

        var result = await HandleAsync(model, version.Id);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.LeftOutChapterIds.ShouldBe([ids[1].Value, ids[3].Value, ids[5].Value]);
        result.Value.TruncatedChapterIds.ShouldBe([ids[0].Value]);
        version.Questions.Select(question => question.Chapter).ShouldBe([ids[0], ids[2], ids[4], ids[0]]);
    }

    [Fact]
    public async Task The_model_is_asked_about_a_pdf_chapters_extracted_text()
    {
        var version = TestDraftVersions.WithChapters(2, "TP", out _);
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(1, 2));

        await HandleAsync(model, version.Id);

        var user = model.Requests.ShouldHaveSingleItem().Where(message => message.Role == ChatRole.User).ShouldHaveSingleItem().Text;
        user.ShouldContain("PDF-Kapitel 2");
        user.ShouldContain("\"source\":\"pdf\"");
        user.ShouldContain("Zytostatika werden ge-");
    }

    [Fact]
    public async Task The_model_is_asked_once_about_the_loaded_versions_text_chapters()
    {
        var version = TestDraftVersions.WithTextChapters(2, "Händehygiene", "Flächen");
        store.Stored.Add(version);
        using var model = new FakeChatClient(Sound(1, 2));

        await HandleAsync(model, version.Id);

        var user = model.Requests.ShouldHaveSingleItem().Where(message => message.Role == ChatRole.User).ShouldHaveSingleItem().Text;
        user.ShouldContain("Händehygiene");
        user.ShouldContain("Flächen");
    }

    [Fact]
    public async Task An_unknown_version_is_not_found_and_the_model_is_not_asked()
    {
        var unknown = CourseVersionId.New();
        using var model = new FakeChatClient(Sound(1));

        var result = await HandleAsync(model, unknown);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.course-version-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        model.Requests.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData("not a draft", HttpStatusCode.Conflict, "editorial.not-a-draft")]
    [InlineData("no text chapters", HttpStatusCode.Conflict, "editorial.no-text-chapters")]
    [InlineData("nothing to draft", HttpStatusCode.Conflict, "editorial.nothing-to-draft")]
    [InlineData("too much content", HttpStatusCode.UnprocessableEntity, "editorial.too-much-content-to-draft")]
    public async Task A_version_that_cannot_be_drafted_is_refused_before_the_model_is_asked_and_stays_unchanged(string state, HttpStatusCode status, string code)
    {
        var version = TestDraftVersions.Undraftable(state);
        store.Stored.Add(version);
        List<QuestionId> before = [.. version.Questions.Select(question => question.Id)];
        using var model = new FakeChatClient(Sound(1));

        var result = await HandleAsync(model, version.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Status.ShouldBe(status);
        result.Error.Code.ShouldBe(code);
        model.Requests.ShouldBeEmpty();
        version.Questions.Select(question => question.Id).ShouldBe(before);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_malformed_answer_is_a_bad_gateway_and_changes_nothing()
    {
        var version = TestDraftVersions.WithTextChapters(3, "Händehygiene");
        var chapter = TestDraftVersions.TextChapterIds(version)[0];
        version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);
        List<QuestionId> before = [.. version.Questions.Select(question => question.Id)];
        using var model = new FakeChatClient(Sound(1));

        var result = await HandleAsync(model, version.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.ai-draft-malformed");
        result.Error.Status.ShouldBe(HttpStatusCode.BadGateway);
        version.Questions.Select(question => question.Id).ShouldBe(before);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_draft_that_lost_the_race_is_told_the_version_changed()
    {
        var version = TestDraftVersions.WithTextChapters(2, "Händehygiene", "Flächen");
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(Failure(LostRaces.VersionMovedOn()));
        using var model = new FakeChatClient(Sound(1, 2));

        var result = await new ApplyAiTestDraftHandler(store, lostRace, model)
            .HandleAsync(new ApplyAiTestDraft(version.Id), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        result.Error.Code.ShouldBe("editorial.course-version-changed");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_failing_model_is_not_caught_and_nothing_is_committed()
    {
        var version = TestDraftVersions.WithTextChapters(2, "Händehygiene", "Flächen");
        var chapter = TestDraftVersions.TextChapterIds(version)[0];
        version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);
        List<QuestionId> before = [.. version.Questions.Select(question => question.Id)];
        using var model = Substitute.For<IChatClient>();
        model.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("The provider answered 500."));

        await Should.ThrowAsync<HttpRequestException>(() => HandleAsync(model, version.Id));

        version.Questions.Select(question => question.Id).ShouldBe(before);
        store.Commits.ShouldBe(0);
    }

    private Task<Result<EditorialTestDraftDto, ApiError>> HandleAsync(IChatClient model, CourseVersionId version) =>
        new ApplyAiTestDraftHandler(store, store, model).HandleAsync(new ApplyAiTestDraft(version), TestContext.Current.CancellationToken);
}
