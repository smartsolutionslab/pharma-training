using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>Course versions in each state, reached through the aggregate's own methods, never by setting a status.</summary>
internal static class CourseVersionBuilder
{
    public static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    public static readonly PersonId Author = PersonId.From(Guid.CreateVersion7());
    public static readonly PersonId Reviewer = PersonId.From(Guid.CreateVersion7());
    public static readonly PersonId Approver = PersonId.From(Guid.CreateVersion7());

    // The display names each person's token carries (#205). Not "ReviewerName": the type of that name is imported too.
    public static readonly ReviewerName AuthorsName = ReviewerName.From("Erika Editor");
    public static readonly ReviewerName ReviewersName = ReviewerName.From("Rita Reviewer");
    public static readonly ReviewerName ApproversName = ReviewerName.From("Anton Approver");

    public static CourseVersion Draft(string title = "Hygiene 2027") =>
        CourseVersion.DraftFirst(CourseVersionId.New(), CourseId.New(), CourseTitle.From(title), Author);

    public static CourseVersion DraftWithChapter(out ChapterId chapter, string title = "Hygiene 2027")
    {
        var version = Draft(title);
        chapter = ChapterId.New();
        version.AddChapter(TextChapter(chapter, "Händehygiene", "Hände vor und nach …")).IsSuccess.ShouldBeTrue();
        return version;
    }

    /// <summary>A draft with one chapter and one approved question, as many as its blueprint asks: ready to be submitted.</summary>
    public static CourseVersion ReadyToSubmit(out ChapterId chapter, string title = "Hygiene 2027")
    {
        var version = DraftWithChapter(out chapter, title);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        return version;
    }

    public static CourseVersion ReadyToSubmit(string title = "Hygiene 2027") => ReadyToSubmit(out _, title);

    /// <summary>A draft with one chapter and one approved question, as many as its blueprint asks, submitted by the author.</summary>
    public static CourseVersion InReview(out ChapterId chapter, string title = "Hygiene 2027")
    {
        var version = ReadyToSubmit(out chapter, title);
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        return version;
    }

    public static CourseVersion InReview(string title = "Hygiene 2027") => InReview(out _, title);

    /// <summary>
    /// A version of two questions, submitted by the author: the author approved the first before submitting, the
    /// given editor the second, under <see cref="ApproversName"/>, before the submission or while the version is in review.
    /// </summary>
    public static CourseVersion InReviewWithASecondApprover(PersonId approver, bool approvedWhileInReview = true)
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(2), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        var first = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        var second = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(first, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        if (!approvedWhileInReview)
        {
            version.ApproveQuestion(second, approver, ApproversName, Now).IsSuccess.ShouldBeTrue();
        }

        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        if (approvedWhileInReview)
        {
            version.ApproveQuestion(second, approver, ApproversName, Now).IsSuccess.ShouldBeTrue();
        }

        return version;
    }

    public static CourseVersion Published(out ChapterId chapter, string title = "Hygiene 2027")
    {
        var version = InReview(out chapter, title);
        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        return version;
    }

    public static CourseVersion Published(string title = "Hygiene 2027") => Published(out _, title);

    /// <summary>The next version of a published one, its copied question still approved, submitted by the author.</summary>
    public static CourseVersion NextInReview(CourseVersion published)
    {
        var next = published.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        return next;
    }

    public static Chapter TextChapter(ChapterId id, string title = "Händehygiene", string text = "Hände vor und nach …") =>
        Chapter.OfText(id, ChapterTitle.From(title), MarkdownText.From(text));

    public static DraftedQuestion SingleChoice(ChapterId chapter) =>
        new(
            QuestionText.From("Wann desinfizieren Sie die Hände?"),
            QuestionType.SingleChoice,
            chapter,
            [(AnswerText.From("Vor jedem Kundenkontakt mit Rezeptur"), Correctness.Correct), (AnswerText.From("Einmal am Tag"), Correctness.Incorrect)]);
}
