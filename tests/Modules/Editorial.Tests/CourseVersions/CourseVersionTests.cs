// Editorial — behaviour of the CourseVersion aggregate: content, the AI test draft, review and publishing.

using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class CourseVersionTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly PersonId Author = PersonId.From(Guid.CreateVersion7());
    private static readonly ReviewerName AuthorsName = ReviewerName.From("Erika Editor");
    private static readonly PersonId Reviewer = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public void A_first_draft_starts_with_the_default_blueprint()
    {
        var version = Draft();

        version.Status.ShouldBe(CourseVersionStatus.Draft);
        version.Number.ShouldBe(VersionNumber.First);
        version.Blueprint.ShouldBe(TestBlueprint.Default);
    }

    [Fact]
    public void An_ai_draft_replaces_unreviewed_questions_but_keeps_approved_ones()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(Blueprint(questions: 3));
        version.ApplyAiTestDraft([Single(chapter), Single(chapter)], NewQuestion, NewOption);
        var kept = version.Questions[0].Id;
        version.ApproveQuestion(kept, Author, AuthorsName, Now);

        version.ApplyAiTestDraft([Single(chapter), Single(chapter)], NewQuestion, NewOption);

        version.Questions.Count.ShouldBe(3);
        version.Questions.ShouldContain(question => question.Id == kept && question.Review == ReviewState.Approved);
        version.Questions.ShouldAllBe(question => question.Provenance == Provenance.AiDrafted);
    }

    [Fact]
    public void Editing_an_approved_question_takes_its_approval_away()
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(NewQuestion(), Single(chapter), NewOption).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now);

        version.EditQuestion(question, Single(chapter), NewOption);

        version.Questions.Single().Review.ShouldBe(ReviewState.Unreviewed);
        version.Questions.Single().ReviewedBy.ShouldBeNull();
    }

    [Fact]
    public void Replacing_a_chapter_sends_its_questions_back_to_review()
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(NewQuestion(), Single(chapter), NewOption).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now);

        version.ReplaceChapter(Chapter.OfText(chapter, ChapterTitle.From("Händehygiene"), MarkdownText.From("Neu: …")));

        version.Questions.Single().Review.ShouldBe(ReviewState.Unreviewed);
    }

    [Fact]
    public void Removing_a_chapter_removes_its_questions()
    {
        var version = DraftWithChapter(out var chapter);
        version.AddQuestion(NewQuestion(), Single(chapter), NewOption);

        version.RemoveChapter(chapter);

        version.Questions.ShouldBeEmpty();
    }

    [Fact]
    public void A_single_choice_question_with_two_right_answers_cannot_be_approved()
    {
        var version = DraftWithChapter(out var chapter);
        var broken = new DraftedQuestion(
            QuestionText.From("Wie oft?"),
            QuestionType.SingleChoice,
            chapter,
            [(AnswerText.From("Täglich"), Correctness.Correct), (AnswerText.From("Stündlich"), Correctness.Correct)]);
        var question = version.AddQuestion(NewQuestion(), broken, NewOption).Value;

        version.ApproveQuestion(question, Author, AuthorsName, Now).Error.ShouldBeOfType<SingleChoiceNeedsOneCorrect>();
    }

    [Fact]
    public void A_test_short_of_its_blueprint_cannot_be_submitted()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(Blueprint(questions: 2));
        version.AddQuestion(NewQuestion(), Single(chapter), NewOption);

        version.SubmitForReview(Author).Error.Value.ShouldBeOfType<TestIncomplete>();
    }

    [Fact]
    public void Publishing_needs_every_question_approved()
    {
        var version = Submitted(approveAll: false);

        version.Publish(Reviewer, ReviewPolicy.SingleEditor, Now).Error.Value.ShouldBeOfType<QuestionNotApproved>();
    }

    [Fact]
    public void With_four_eyes_the_submitter_cannot_publish()
    {
        var version = Submitted(approveAll: true);

        version.Publish(Author, ReviewPolicy.FourEyes, Now).Error.Value.ShouldBeOfType<FourEyesRequired>();
        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_single_editor_may_publish_what_they_submitted()
    {
        var version = Submitted(approveAll: true);

        version.Publish(Author, ReviewPolicy.SingleEditor, Now).IsSuccess.ShouldBeTrue();

        version.DomainEvents.OfType<CourseVersionPublished>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_published_version_cannot_be_edited()
    {
        var version = Submitted(approveAll: true);
        version.Publish(Author, ReviewPolicy.SingleEditor, Now);

        version.Rename(CourseTitle.From("Anders")).Error.ShouldBeOfType<NotADraft>();
    }

    [Fact]
    public void The_next_version_copies_content_and_approvals_with_new_ids()
    {
        var published = Submitted(approveAll: true);
        published.Publish(Author, ReviewPolicy.SingleEditor, Now);

        var next = published.DraftNext(CourseVersionId.New(), Author, NewChapter, NewQuestion, NewOption).Value;

        next.Number.ShouldBe(VersionNumber.From(2));
        next.Status.ShouldBe(CourseVersionStatus.Draft);
        next.Chapters.Single().Id.ShouldNotBe(published.Chapters.Single().Id);
        next.Questions.Single().Chapter.ShouldBe(next.Chapters.Single().Id);
        next.Questions.Single().Review.ShouldBe(ReviewState.Approved);
    }

    [Fact]
    public void Only_a_draft_accepts_uploads()
    {
        var draft = Draft();
        var inReview = Submitted(approveAll: true);
        var published = Submitted(approveAll: true);
        published.Publish(Author, ReviewPolicy.SingleEditor, Now).IsSuccess.ShouldBeTrue();

        draft.AcceptsUploads().IsSuccess.ShouldBeTrue();
        foreach (var refusing in new[] { inReview, published })
        {
            var refused = refusing.AcceptsUploads();
            refused.IsFailure.ShouldBeTrue($"a version {refusing.Status} accepts no upload");
            refused.Error.ShouldBeOfType<NotADraft>();
        }
    }

    [Fact]
    public void A_pdf_and_a_video_chapter_are_added_with_their_kind_file_and_duration()
    {
        var version = Draft();
        var pdf = NewChapter();
        var video = NewChapter();

        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From("course-versions/a/plan.pdf"))).IsSuccess.ShouldBeTrue();
        version.AddChapter(Chapter.OfVideo(video, ChapterTitle.From("Vorführung"), BlobReference.From("course-versions/a/demo.mp4"), VideoDuration.From(90))).IsSuccess.ShouldBeTrue();

        version.Chapters[0].Kind.ShouldBe(ChapterKind.Pdf);
        version.Chapters[0].File.ShouldBe(BlobReference.From("course-versions/a/plan.pdf"));
        version.Chapters[0].Duration.ShouldBeNull();
        version.Chapters[0].Text.ShouldBeNull();
        version.Chapters[1].Kind.ShouldBe(ChapterKind.Video);
        version.Chapters[1].File.ShouldBe(BlobReference.From("course-versions/a/demo.mp4"));
        version.Chapters[1].Duration.ShouldBe(VideoDuration.From(90));
    }

    [Fact]
    public void A_text_chapter_replaced_by_a_video_and_then_a_pdf_keeps_only_the_new_kinds_content()
    {
        var version = DraftWithChapter(out var chapter);

        version.ReplaceChapter(Chapter.OfVideo(chapter, ChapterTitle.From("Vorführung"), BlobReference.From("course-versions/a/demo.mp4"), VideoDuration.From(90))).IsSuccess.ShouldBeTrue();
        var asVideo = version.Chapters.Single();
        (asVideo.Kind, asVideo.File, asVideo.Duration, asVideo.Text).ShouldBe((ChapterKind.Video, BlobReference.From("course-versions/a/demo.mp4"), VideoDuration.From(90), (MarkdownText?)null));

        version.ReplaceChapter(Chapter.OfPdf(chapter, ChapterTitle.From("Hygieneplan"), BlobReference.From("course-versions/a/plan.pdf"))).IsSuccess.ShouldBeTrue();
        var asPdf = version.Chapters.Single();
        (asPdf.Kind, asPdf.File, asPdf.Duration, asPdf.Text).ShouldBe((ChapterKind.Pdf, BlobReference.From("course-versions/a/plan.pdf"), (VideoDuration?)null, (MarkdownText?)null));
    }

    private static CourseVersion Draft() =>
        CourseVersion.DraftFirst(CourseVersionId.New(), CourseId.From(Guid.CreateVersion7()), CourseTitle.From("Hygiene 2027"), Author);

    private static CourseVersion DraftWithChapter(out ChapterId chapter)
    {
        var version = Draft();
        chapter = NewChapter();
        version.AddChapter(Chapter.OfText(chapter, ChapterTitle.From("Händehygiene"), MarkdownText.From("Hände vor und nach …")));
        return version;
    }

    private static CourseVersion Submitted(bool approveAll)
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(Blueprint(questions: 1));
        var question = version.AddQuestion(NewQuestion(), Single(chapter), NewOption).Value;
        if (approveAll)
        {
            version.ApproveQuestion(question, Author, AuthorsName, Now);
        }

        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        return version;
    }

    private static TestBlueprint Blueprint(int questions) =>
        new(QuestionCount.From(questions), ScorePercent.From(80), RecurrenceInterval.Yearly);

    private static DraftedQuestion Single(ChapterId chapter) =>
        new(
            QuestionText.From("Wann desinfizieren Sie die Hände?"),
            QuestionType.SingleChoice,
            chapter,
            [(AnswerText.From("Vor jedem Kundenkontakt mit Rezeptur"), Correctness.Correct), (AnswerText.From("Einmal am Tag"), Correctness.Incorrect)]);

    private static ChapterId NewChapter() => ChapterId.From(Guid.CreateVersion7());

    private static QuestionId NewQuestion() => QuestionId.From(Guid.CreateVersion7());

    private static AnswerOptionId NewOption() => AnswerOptionId.From(Guid.CreateVersion7());
}
