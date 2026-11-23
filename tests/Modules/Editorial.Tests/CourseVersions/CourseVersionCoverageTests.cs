using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>The rules of the CourseVersion aggregate the reference tests do not reach.</summary>
public class CourseVersionCoverageTests
{
    [Fact]
    public void A_first_draft_holds_its_course_title_and_editor_and_nothing_else_yet()
    {
        var id = CourseVersionId.New();
        var course = CourseId.New();

        var version = CourseVersion.DraftFirst(id, course, CourseTitle.From("Hygiene 2027"), Author);

        version.Id.ShouldBe(id);
        version.Course.ShouldBe(course);
        version.Title.ShouldBe(CourseTitle.From("Hygiene 2027"));
        version.CreatedBy.ShouldBe(Author);
        version.Chapters.ShouldBeEmpty();
        version.Questions.ShouldBeEmpty();
        version.SubmittedBy.ShouldBeNull();
        version.PublishedBy.ShouldBeNull();
        version.PublishedAt.ShouldBeNull();
        version.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("add a chapter")]
    [InlineData("replace a chapter")]
    [InlineData("remove a chapter")]
    [InlineData("reorder the chapters")]
    [InlineData("rename")]
    [InlineData("set the blueprint")]
    [InlineData("apply an ai test draft")]
    [InlineData("add a question")]
    [InlineData("edit a question")]
    [InlineData("remove a question")]
    public void Only_a_draft_changes_its_content(string edit)
    {
        var inReview = InReview(out var reviewedChapter);
        var published = Published(out var publishedChapter);

        var refusedInReview = Refusal(inReview, reviewedChapter, edit);
        var refusedPublished = Refusal(published, publishedChapter, edit);

        refusedInReview.ShouldBe(new NotADraft(inReview.Id, CourseVersionStatus.InReview));
        refusedInReview.Code.ShouldBe("editorial.not-a-draft");
        refusedPublished.ShouldBe(new NotADraft(published.Id, CourseVersionStatus.Published));
        inReview.Chapters.ShouldHaveSingleItem().Id.ShouldBe(reviewedChapter);
        inReview.Questions.ShouldHaveSingleItem();
        inReview.Title.ShouldBe(CourseTitle.From("Hygiene 2027"));
        published.Chapters.ShouldHaveSingleItem().Id.ShouldBe(publishedChapter);
        published.Questions.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_question_of_a_published_version_is_no_longer_approved()
    {
        var published = Published();

        published.ApproveQuestion(published.Questions.Single().Id, Author, AuthorsName, Now).Error
            .ShouldBe(new NotADraft(published.Id, CourseVersionStatus.Published));
    }

    [Fact]
    public void A_chapter_is_added_at_the_end_and_its_id_answered()
    {
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();

        var added = version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen, nicht sprühen."));

        added.Value.ShouldBe(second);
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([first, second]);
        var chapter = version.Chapters[1];
        chapter.Title.ShouldBe(ChapterTitle.From("Flächendesinfektion"));
        chapter.Kind.ShouldBe(ChapterKind.Text);
        chapter.Text.ShouldBe(MarkdownText.From("Wischen, nicht sprühen."));
        chapter.File.ShouldBeNull();
        chapter.Duration.ShouldBeNull();
    }

    [Fact]
    public void A_pdf_chapter_holds_its_file_and_a_video_chapter_its_file_and_length()
    {
        var pdf = Chapter.OfPdf(ChapterId.New(), ChapterTitle.From("Hygieneplan"), BlobReference.From("courses/plan.pdf"));
        var video = Chapter.OfVideo(ChapterId.New(), ChapterTitle.From("Vorführung"), BlobReference.From("courses/demo.mp4"), VideoDuration.From(90));

        pdf.Kind.ShouldBe(ChapterKind.Pdf);
        pdf.File.ShouldBe(BlobReference.From("courses/plan.pdf"));
        pdf.Text.ShouldBeNull();
        pdf.Duration.ShouldBeNull();
        video.Kind.ShouldBe(ChapterKind.Video);
        video.File.ShouldBe(BlobReference.From("courses/demo.mp4"));
        video.Duration.ShouldBe(VideoDuration.From(90));
        video.Text.ShouldBeNull();
    }

    [Fact]
    public void Replacing_a_chapter_changes_its_content_in_its_place()
    {
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        var stored = version.Chapters[0];

        var replaced = version.ReplaceChapter(Chapter.OfPdf(first, ChapterTitle.From("Hygieneplan"), BlobReference.From("courses/plan.pdf")));

        replaced.IsSuccess.ShouldBeTrue();
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([first, second]);
        var chapter = version.Chapters[0];
        chapter.ShouldBeSameAs(stored);
        chapter.Title.ShouldBe(ChapterTitle.From("Hygieneplan"));
        chapter.Kind.ShouldBe(ChapterKind.Pdf);
        chapter.File.ShouldBe(BlobReference.From("courses/plan.pdf"));
        chapter.Text.ShouldBeNull();
        chapter.Duration.ShouldBeNull();
    }

    [Fact]
    public void Replacing_a_chapter_with_a_video_takes_its_length_too()
    {
        var version = DraftWithChapter(out var chapter);

        version.ReplaceChapter(Chapter.OfVideo(chapter, ChapterTitle.From("Vorführung"), BlobReference.From("courses/demo.mp4"), VideoDuration.From(90)))
            .IsSuccess.ShouldBeTrue();

        var replaced = version.Chapters.ShouldHaveSingleItem();
        replaced.Kind.ShouldBe(ChapterKind.Video);
        replaced.Duration.ShouldBe(VideoDuration.From(90));
        replaced.Text.ShouldBeNull();
    }

    [Fact]
    public void A_chapter_the_version_does_not_hold_is_neither_replaced_nor_removed()
    {
        var version = DraftWithChapter(out var chapter);
        var stranger = ChapterId.New();

        var replaced = version.ReplaceChapter(TextChapter(stranger));
        var removed = version.RemoveChapter(stranger);

        replaced.Error.ShouldBe(new ChapterNotFound(version.Id, stranger));
        replaced.Error.Code.ShouldBe("editorial.chapter-not-found");
        removed.Error.ShouldBe(new ChapterNotFound(version.Id, stranger));
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(chapter);
    }

    [Fact]
    public void Removing_a_chapter_keeps_the_others_and_their_questions()
    {
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        var kept = version.AddQuestion(QuestionId.New(), SingleChoice(second), AnswerOptionId.New).Value;

        version.RemoveChapter(first).IsSuccess.ShouldBeTrue();

        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(second);
        version.Questions.ShouldHaveSingleItem().Id.ShouldBe(kept);
    }

    [Fact]
    public void Chapters_are_reordered_to_the_order_given()
    {
        var (version, first, second, third) = ThreeChapters();

        var reordered = version.ReorderChapters([third, first, second]);

        reordered.IsSuccess.ShouldBeTrue();
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([third, first, second]);
    }

    [Fact]
    public void An_order_that_misses_a_chapter_repeats_one_or_names_a_stranger_is_refused()
    {
        var (version, first, second, third) = ThreeChapters();

        var missing = version.ReorderChapters([third, first]);
        var repeated = version.ReorderChapters([third, first, first]);
        var stranger = version.ReorderChapters([third, first, ChapterId.New()]);
        var longer = version.ReorderChapters([third, first, second, second]);

        missing.Error.ShouldBe(new OrderDoesNotMatchChapters(version.Id));
        missing.Error.Code.ShouldBe("editorial.order-does-not-match");
        repeated.Error.ShouldBe(new OrderDoesNotMatchChapters(version.Id));
        stranger.Error.ShouldBe(new OrderDoesNotMatchChapters(version.Id));
        longer.Error.ShouldBe(new OrderDoesNotMatchChapters(version.Id));
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([first, second, third]);
    }

    [Fact]
    public void A_draft_is_renamed_and_given_a_blueprint()
    {
        var version = Draft();
        var blueprint = new TestBlueprint(QuestionCount.From(5), ScorePercent.From(70), RecurrenceInterval.From(24));

        version.Rename(CourseTitle.From("Hygiene 2028")).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(blueprint).IsSuccess.ShouldBeTrue();

        version.Title.ShouldBe(CourseTitle.From("Hygiene 2028"));
        version.Blueprint.ShouldBe(blueprint);
    }

    [Fact]
    public void A_question_an_editor_adds_is_human_unreviewed_and_holds_its_options()
    {
        var version = DraftWithChapter(out var chapter);
        var id = QuestionId.New();

        var added = version.AddQuestion(id, SingleChoice(chapter), AnswerOptionId.New);

        added.Value.ShouldBe(id);
        var question = version.Questions.ShouldHaveSingleItem();
        question.Id.ShouldBe(id);
        question.Text.ShouldBe(QuestionText.From("Wann desinfizieren Sie die Hände?"));
        question.Type.ShouldBe(QuestionType.SingleChoice);
        question.Chapter.ShouldBe(chapter);
        question.Provenance.ShouldBe(Provenance.Human);
        question.Review.ShouldBe(ReviewState.Unreviewed);
        question.ReviewedAt.ShouldBeNull();
        question.Options.Select(option => (option.Text, option.Correctness)).ShouldBe(
            [(AnswerText.From("Vor jedem Kundenkontakt mit Rezeptur"), Correctness.Correct), (AnswerText.From("Einmal am Tag"), Correctness.Incorrect)]);
        question.Options.Select(option => option.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void A_question_about_a_chapter_the_version_does_not_hold_is_refused()
    {
        var version = DraftWithChapter(out _);
        var stranger = ChapterId.New();

        var added = version.AddQuestion(QuestionId.New(), SingleChoice(stranger), AnswerOptionId.New);

        added.Error.ShouldBe(new ChapterNotFound(version.Id, stranger));
        version.Questions.ShouldBeEmpty();
    }

    [Fact]
    public void Editing_a_question_changes_its_text_type_and_options()
    {
        var version = DraftWithChapter(out var chapter);
        var id = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        var multiple = new DraftedQuestion(
            QuestionText.From("Was gehört zur Händehygiene?"),
            QuestionType.MultipleChoice,
            chapter,
            [(AnswerText.From("Waschen"), Correctness.Correct), (AnswerText.From("Desinfizieren"), Correctness.Correct), (AnswerText.From("Nichts"), Correctness.Incorrect)]);

        version.EditQuestion(id, multiple, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var question = version.Questions.ShouldHaveSingleItem();
        question.Text.ShouldBe(QuestionText.From("Was gehört zur Händehygiene?"));
        question.Type.ShouldBe(QuestionType.MultipleChoice);
        question.Options.Count.ShouldBe(3);
        question.Provenance.ShouldBe(Provenance.Human);
    }

    [Fact]
    public void A_question_the_version_does_not_hold_is_neither_edited_removed_nor_approved()
    {
        var version = DraftWithChapter(out var chapter);
        var stranger = QuestionId.New();

        var edited = version.EditQuestion(stranger, SingleChoice(chapter), AnswerOptionId.New);
        var removed = version.RemoveQuestion(stranger);
        var approved = version.ApproveQuestion(stranger, Author, AuthorsName, Now);

        edited.Error.ShouldBe(new QuestionNotFound(version.Id, stranger));
        edited.Error.Code.ShouldBe("editorial.question-not-found");
        removed.Error.ShouldBe(new QuestionNotFound(version.Id, stranger));
        approved.Error.ShouldBe(new QuestionNotFound(version.Id, stranger));
    }

    [Fact]
    public void A_removed_question_is_gone()
    {
        var version = DraftWithChapter(out var chapter);
        var id = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;

        version.RemoveQuestion(id).IsSuccess.ShouldBeTrue();

        version.Questions.ShouldBeEmpty();
    }

    [Fact]
    public void An_approval_records_who_approved_and_when()
    {
        var version = DraftWithChapter(out var chapter);
        var id = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;

        version.ApproveQuestion(id, Reviewer, ReviewersName, Now).IsSuccess.ShouldBeTrue();

        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Approved);
        question.ReviewedBy.ShouldBe(Reviewer);
        question.ReviewedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_question_with_fewer_than_two_options_cannot_be_approved()
    {
        var version = DraftWithChapter(out var chapter);
        var lonely = new DraftedQuestion(QuestionText.From("Wie oft?"), QuestionType.SingleChoice, chapter, [(AnswerText.From("Täglich"), Correctness.Correct)]);
        var id = version.AddQuestion(QuestionId.New(), lonely, AnswerOptionId.New).Value;

        var approved = version.ApproveQuestion(id, Author, AuthorsName, Now);

        approved.Error.ShouldBe(new TooFewOptions(id));
        approved.Error.Code.ShouldBe("editorial.too-few-options");
        version.Questions.Single().Review.ShouldBe(ReviewState.Unreviewed);
    }

    [Fact]
    public void A_multiple_choice_question_needs_a_right_answer_to_be_approved()
    {
        var version = DraftWithChapter(out var chapter);
        var wrong = new DraftedQuestion(
            QuestionText.From("Was gehört zur Händehygiene?"),
            QuestionType.MultipleChoice,
            chapter,
            [(AnswerText.From("Nichts"), Correctness.Incorrect), (AnswerText.From("Gar nichts"), Correctness.Incorrect)]);
        var right = new DraftedQuestion(
            QuestionText.From("Was gehört zur Händehygiene?"),
            QuestionType.MultipleChoice,
            chapter,
            [(AnswerText.From("Waschen"), Correctness.Correct), (AnswerText.From("Nichts"), Correctness.Incorrect)]);
        var refused = version.AddQuestion(QuestionId.New(), wrong, AnswerOptionId.New).Value;
        var accepted = version.AddQuestion(QuestionId.New(), right, AnswerOptionId.New).Value;

        var refusal = version.ApproveQuestion(refused, Author, AuthorsName, Now);
        var approval = version.ApproveQuestion(accepted, Author, AuthorsName, Now);

        refusal.Error.ShouldBe(new MultipleChoiceNeedsACorrect(refused));
        refusal.Error.Code.ShouldBe("editorial.multiple-choice-a-correct");
        approval.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_question_in_review_can_still_be_approved()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        var id = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        version.ApproveQuestion(id, Reviewer, ReviewersName, Now).IsSuccess.ShouldBeTrue();

        version.Questions.Single().Review.ShouldBe(ReviewState.Approved);
    }

    [Fact]
    public void An_ai_draft_about_a_chapter_the_version_does_not_hold_is_refused_whole()
    {
        var version = DraftWithChapter(out var chapter);
        var stranger = ChapterId.New();

        var applied = version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(stranger)], QuestionId.New, AnswerOptionId.New);

        applied.Error.ShouldBe(new ChapterNotFound(version.Id, stranger));
        version.Questions.ShouldBeEmpty();
        version.DomainEvents.OfType<AiTestDraftApplied>().ShouldBeEmpty();
    }

    [Fact]
    public void An_applied_ai_draft_is_recorded_with_how_many_questions_it_drafted()
    {
        var version = DraftWithChapter(out var chapter);

        version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        version.DomainEvents.OfType<AiTestDraftApplied>().ShouldHaveSingleItem().ShouldBe(new AiTestDraftApplied(version.Id, QuestionCount.From(2)));
    }

    [Fact]
    public void A_version_without_chapters_cannot_be_submitted()
    {
        var version = Draft();

        var submitted = version.SubmitForReview(Author);

        submitted.Error.Value.ShouldBe(new NoChapters(version.Id));
        submitted.Error.Value.ShouldBeOfType<NoChapters>().Code.ShouldBe("editorial.no-chapters");
        version.Status.ShouldBe(CourseVersionStatus.Draft);
    }

    [Fact]
    public void A_test_short_of_its_blueprint_names_how_many_questions_it_needs()
    {
        var version = DraftWithChapter(out _);

        var submitted = version.SubmitForReview(Author);

        var incomplete = submitted.Error.Value.ShouldBeOfType<TestIncomplete>();
        incomplete.ShouldBe(new TestIncomplete(version.Id, QuestionCount.Default, QuestionCount.From(0)));
        incomplete.Code.ShouldBe("editorial.test-incomplete");
        incomplete.Message.ShouldBe("Der Test braucht 10 Fragen.");
    }

    [Fact]
    public void Submitting_puts_the_version_in_review_and_records_who_submitted_it()
    {
        var version = InReview();

        version.Status.ShouldBe(CourseVersionStatus.InReview);
        version.SubmittedBy.ShouldBe(Author);
        version.DomainEvents.OfType<SubmittedForReview>().ShouldHaveSingleItem().ShouldBe(new SubmittedForReview(version.Id, Author));
    }

    [Fact]
    public void A_version_in_review_is_not_submitted_again()
    {
        var version = InReview();

        version.SubmitForReview(Author).Error.Value.ShouldBe(new NotADraft(version.Id, CourseVersionStatus.InReview));
    }

    [Fact]
    public void A_version_in_review_returns_to_draft_and_a_draft_does_not()
    {
        var draft = Draft();
        var inReview = InReview();

        var fromDraft = draft.ReturnToDraft(Reviewer, ReturnReason.From("Kapitel 2 braucht eine Quelle."), Now);
        var fromReview = inReview.ReturnToDraft(Reviewer, ReturnReason.From("Kapitel 2 braucht eine Quelle."), Now);

        fromDraft.Error.Value.ShouldBe(new NotInReview(draft.Id, CourseVersionStatus.Draft));
        fromDraft.Error.Value.ShouldBeOfType<NotInReview>().Code.ShouldBe("editorial.not-in-review");
        fromReview.IsSuccess.ShouldBeTrue();
        inReview.Status.ShouldBe(CourseVersionStatus.Draft);
    }

    [Fact]
    public void A_draft_is_not_published()
    {
        var version = DraftWithChapter(out _);

        version.Publish(Reviewer, ReviewPolicy.SingleEditor, Now).Error.Value.ShouldBe(new NotInReview(version.Id, CourseVersionStatus.Draft));
        version.Status.ShouldBe(CourseVersionStatus.Draft);
    }

    [Fact]
    public void Publishing_records_who_published_and_when_and_announces_the_version()
    {
        var version = InReview();

        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Reviewer);
        version.PublishedAt.ShouldBe(Now);
        version.DomainEvents.OfType<CourseVersionPublished>().ShouldHaveSingleItem()
            .ShouldBe(new CourseVersionPublished(version.Id, version.Course, VersionNumber.First, Now));
    }

    [Fact]
    public void The_codes_of_the_publishing_refusals_are_stable()
    {
        var unapproved = Draft();
        var chapter = ChapterId.New();
        unapproved.AddChapter(TextChapter(chapter)).IsSuccess.ShouldBeTrue();
        unapproved.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        unapproved.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        unapproved.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        var approved = InReview();

        unapproved.Publish(Reviewer, ReviewPolicy.SingleEditor, Now).Error.Value.ShouldBeOfType<QuestionNotApproved>().Code.ShouldBe("editorial.question-not-approved");
        approved.Publish(Author, ReviewPolicy.FourEyes, Now).Error.Value.ShouldBeOfType<FourEyesRequired>().Code.ShouldBe("editorial.four-eyes");
    }

    [Fact]
    public void A_draft_is_not_retired()
    {
        var version = Draft();

        var retired = version.Retire(Reviewer, Now);

        retired.Error.Value.ShouldBe(new NotPublished(version.Id, CourseVersionStatus.Draft));
        retired.Error.Value.ShouldBeOfType<NotPublished>().Code.ShouldBe("editorial.not-published");
        version.Status.ShouldBe(CourseVersionStatus.Draft);
    }

    [Fact]
    public void A_published_version_is_retired_and_its_retirement_announced()
    {
        var version = Published();

        version.Retire(Reviewer, Now).IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CourseVersionStatus.Retired);
        version.DomainEvents.OfType<CourseVersionRetired>().ShouldHaveSingleItem().ShouldBe(new CourseVersionRetired(version.Id, version.Course, Reviewer));
    }

    [Fact]
    public void A_superseded_version_is_retired_too()
    {
        var version = Published();
        version.Supersede();

        version.Retire(Reviewer, Now).IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CourseVersionStatus.Retired);
        version.DomainEvents.OfType<CourseVersionRetired>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Only_a_published_version_is_superseded()
    {
        var draft = Draft();
        var inReview = InReview();
        var published = Published();

        draft.Supersede();
        inReview.Supersede();
        published.Supersede();

        draft.Status.ShouldBe(CourseVersionStatus.Draft);
        inReview.Status.ShouldBe(CourseVersionStatus.InReview);
        published.Status.ShouldBe(CourseVersionStatus.Superseded);
    }

    [Fact]
    public void A_retired_version_is_neither_superseded_nor_retired_again()
    {
        var version = Published();
        version.Retire(Reviewer, Now).IsSuccess.ShouldBeTrue();

        version.Supersede();
        var again = version.Retire(Reviewer, Now);

        version.Status.ShouldBe(CourseVersionStatus.Retired);
        again.Error.Value.ShouldBe(new NotPublished(version.Id, CourseVersionStatus.Retired));
    }

    [Fact]
    public void Only_a_published_version_has_a_next_one()
    {
        var draft = Draft();
        var inReview = InReview();

        var fromDraft = draft.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New);
        var fromReview = inReview.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New);

        fromDraft.Error.ShouldBe(new NotPublished(draft.Id, CourseVersionStatus.Draft));
        fromReview.Error.ShouldBe(new NotPublished(inReview.Id, CourseVersionStatus.InReview));
    }

    [Fact]
    public void The_next_version_is_a_draft_of_the_same_course_by_the_editor_who_started_it()
    {
        var published = Published(out var chapter);
        var id = CourseVersionId.New();

        var next = published.DraftNext(id, Reviewer, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;

        next.Id.ShouldBe(id);
        next.Course.ShouldBe(published.Course);
        next.Title.ShouldBe(published.Title);
        next.Blueprint.ShouldBe(published.Blueprint);
        next.CreatedBy.ShouldBe(Reviewer);
        next.SubmittedBy.ShouldBeNull();
        next.PublishedBy.ShouldBeNull();
        var copied = next.Chapters.ShouldHaveSingleItem();
        copied.Title.ShouldBe(published.Chapters.Single().Title);
        copied.Text.ShouldBe(published.Chapters.Single().Text);
        copied.Id.ShouldNotBe(chapter);
        var question = next.Questions.ShouldHaveSingleItem();
        question.Id.ShouldNotBe(published.Questions.Single().Id);
        question.ReviewedBy.ShouldBe(Author);
        question.ReviewedAt.ShouldBe(Now);
        question.Options.Select(option => option.Id).ShouldNotContain(published.Questions.Single().Options[0].Id);
        published.Status.ShouldBe(CourseVersionStatus.Published);
    }

    private static DomainError Refusal(CourseVersion version, ChapterId chapter, string edit) => edit switch
    {
        "add a chapter" => version.AddChapter(TextChapter(ChapterId.New())).Error,
        "replace a chapter" => version.ReplaceChapter(TextChapter(chapter, "Neu", "Neu: …")).Error,
        "remove a chapter" => version.RemoveChapter(chapter).Error,
        "reorder the chapters" => version.ReorderChapters([chapter]).Error,
        "rename" => version.Rename(CourseTitle.From("Anders")).Error,
        "set the blueprint" => version.SetBlueprint(TestBlueprint.Default).Error,
        "apply an ai test draft" => version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Error,
        "add a question" => version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Error,
        "edit a question" => version.EditQuestion(version.Questions.Single().Id, SingleChoice(chapter), AnswerOptionId.New).Error,
        "remove a question" => version.RemoveQuestion(version.Questions.Single().Id).Error,
        _ => throw new InvalidOperationException($"No edit is called '{edit}'."),
    };

    private static (CourseVersion Version, ChapterId First, ChapterId Second, ChapterId Third) ThreeChapters()
    {
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        var third = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(third, "Arbeitskleidung", "Kittel wechseln.")).IsSuccess.ShouldBeTrue();
        return (version, first, second, third);
    }
}
