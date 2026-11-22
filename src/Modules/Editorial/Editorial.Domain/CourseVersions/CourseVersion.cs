using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// One version of a course, from the first draft to its retirement. Once published it never changes: every change
/// after that is a new version, started with <see cref="DraftNext"/>.
/// </summary>
public sealed class CourseVersion : AggregateRoot<CourseVersionId>
{
    private readonly List<Chapter> chapters = [];
    private readonly List<Question> questions = [];

    private CourseVersion(CourseVersionId id, CourseId course, VersionNumber number, CourseTitle title, TestBlueprint blueprint, PersonId createdBy)
        : this(id)
    {
        Course = course;
        Number = number;
        Title = title;
        Blueprint = blueprint;
        CreatedBy = createdBy;
        Status = CourseVersionStatus.Draft;
    }

    /// <summary>For EF Core, which sets the rest through the backing fields.</summary>
    private CourseVersion(CourseVersionId id)
        : base(id)
    {
    }

    public CourseId Course { get; } = default!;

    public VersionNumber Number { get; } = default!;

    public CourseTitle Title { get; private set; } = default!;

    public TestBlueprint Blueprint { get; private set; } = default!;

    public CourseVersionStatus Status { get; private set; }

    public IReadOnlyList<Chapter> Chapters => chapters;

    /// <summary>A read-only view: a cast does not lead back to the list behind it.</summary>
    public IReadOnlyList<Question> Questions => questions.AsReadOnly();

    public PersonId CreatedBy { get; } = default!;

    public PersonId? SubmittedBy { get; private set; }

    public PersonId? PublishedBy { get; private set; }

    public Timestamp? PublishedAt { get; private set; }

    /// <summary>The editor who last returned the version from review to draft; a later return overwrites it.</summary>
    public PersonId? ReturnedBy { get; private set; }

    public Timestamp? ReturnedAt { get; private set; }

    public PersonId? RetiredBy { get; private set; }

    public Timestamp? RetiredAt { get; private set; }

    private bool IsDraft => Status == CourseVersionStatus.Draft;

    public static CourseVersion DraftFirst(CourseVersionId id, CourseId course, CourseTitle title, PersonId editor) =>
        new(id, course, VersionNumber.First, title, TestBlueprint.Default, editor);

    /// <summary>
    /// The next version of a published one: a copy of its chapters and questions with new ids. Approved questions
    /// stay approved until their chapter or they themselves change.
    /// </summary>
    public Result<CourseVersion, DomainError> DraftNext(
        CourseVersionId id,
        PersonId editor,
        Func<ChapterId> newChapterId,
        Func<QuestionId> newQuestionId,
        Func<AnswerOptionId> newOptionId)
    {
        Ensure.That(newChapterId).IsNotNull();
        Ensure.That(newQuestionId).IsNotNull();

        if (Status != CourseVersionStatus.Published)
        {
            return Refused(CourseVersionErrors.NotPublished(Id, Status));
        }

        var next = new CourseVersion(id, Course, Number.Next(), Title, Blueprint, editor);
        var chapterIds = new Dictionary<ChapterId, ChapterId>();
        foreach (var chapter in chapters)
        {
            var copy = chapter.CopyFor(newChapterId());
            chapterIds[chapter.Id] = copy.Id;
            next.chapters.Add(copy);
        }

        next.questions.AddRange(questions.Select(question => question.CopyFor(newQuestionId(), chapterIds[question.Chapter], newOptionId)));
        return Success(next);
    }

    // ── Content ──

    /// <summary>Whether a chapter's file may be uploaded for the version: only while it is a draft, which alone takes chapters.</summary>
    public Result<Unit, DomainError> AcceptsUploads()
    {
        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        return Success(Unit.Value);
    }

    public Result<ChapterId, DomainError> AddChapter(Chapter chapter)
    {
        Ensure.That(chapter).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        chapters.Add(chapter);
        return Success(chapter.Id);
    }

    /// <summary>Replaces a chapter's content. Its questions need a fresh review, since what they ask about changed.</summary>
    public Result<Unit, DomainError> ReplaceChapter(Chapter replacement)
    {
        Ensure.That(replacement).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (chapters.SingleOrDefault(chapter => chapter.Id == replacement.Id) is not { } stored)
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, replacement.Id));
        }

        stored.Take(replacement);
        foreach (var question in questions.Where(question => question.Chapter == replacement.Id))
        {
            question.MarkUnreviewed();
        }

        return Success(Unit.Value);
    }

    /// <summary>Removes a chapter and the questions that ask about it.</summary>
    public Result<Unit, DomainError> RemoveChapter(ChapterId chapter)
    {
        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (chapters.RemoveAll(existing => existing.Id == chapter) == 0)
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, chapter));
        }

        questions.RemoveAll(question => question.Chapter == chapter);
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> ReorderChapters(IReadOnlyList<ChapterId> order)
    {
        Ensure.That(order).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (order.Count != chapters.Count || !order.ToHashSet().SetEquals(chapters.Select(chapter => chapter.Id)))
        {
            return Failure(CourseVersionErrors.OrderDoesNotMatchChapters(Id));
        }

        var byId = chapters.ToDictionary(chapter => chapter.Id);
        chapters.Clear();
        chapters.AddRange(order.Select(id => byId[id]));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> Rename(CourseTitle title)
    {
        Ensure.That(title).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        Title = title;
        return Success(Unit.Value);
    }

    // ── Test ──

    public Result<Unit, DomainError> SetBlueprint(TestBlueprint blueprint)
    {
        Ensure.That(blueprint).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        Blueprint = blueprint;
        return Success(Unit.Value);
    }

    /// <summary>
    /// Takes the LLM's draft of the whole test. Approved questions stay; the unreviewed ones are replaced, so a new
    /// draft never throws away work an editor has signed off. Answers the drafted questions' new ids, in the draft's order.
    /// </summary>
    public Result<IReadOnlyList<QuestionId>, DomainError> ApplyAiTestDraft(IReadOnlyList<DraftedQuestion> drafted, Func<QuestionId> newQuestionId, Func<AnswerOptionId> newOptionId)
    {
        Ensure.That(drafted).IsNotNull();
        Ensure.That(newQuestionId).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (drafted.FirstOrDefault(draft => chapters.All(chapter => chapter.Id != draft.Chapter)) is { } stray)
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, stray.Chapter));
        }

        if (questions.Count(question => question.Review != ReviewState.Unreviewed) + drafted.Count > QuestionCount.Maximum)
        {
            return Refused(CourseVersionErrors.TooManyQuestions(Id));
        }

        List<Question> added = [.. drafted.Select(draft => Question.From(newQuestionId(), draft, Provenance.AiDrafted, newOptionId))];
        questions.RemoveAll(question => question.Review == ReviewState.Unreviewed);
        questions.AddRange(added);
        Raise(new AiTestDraftApplied(Id, QuestionCount.From(drafted.Count)));
        IReadOnlyList<QuestionId> ids = [.. added.Select(question => question.Id)];
        return Success(ids);
    }

    public Result<QuestionId, DomainError> AddQuestion(QuestionId id, DraftedQuestion draft, Func<AnswerOptionId> newOptionId)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(draft).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (chapters.All(chapter => chapter.Id != draft.Chapter))
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, draft.Chapter));
        }

        if (questions.Count >= QuestionCount.Maximum)
        {
            return Refused(CourseVersionErrors.TooManyQuestions(Id));
        }

        questions.Add(Question.From(id, draft, Provenance.Human, newOptionId));
        return Success(id);
    }

    public Result<Unit, DomainError> EditQuestion(QuestionId id, DraftedQuestion draft, Func<AnswerOptionId> newOptionId)
    {
        Ensure.That(draft).IsNotNull();

        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (questions.SingleOrDefault(question => question.Id == id) is not { } question)
        {
            return Failure(CourseVersionErrors.QuestionNotFound(Id, id));
        }

        // Checked last, as AddQuestion does: a version that is no draft or lacks the question says so first.
        if (chapters.All(chapter => chapter.Id != draft.Chapter))
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, draft.Chapter));
        }

        question.Edit(draft, newOptionId);
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> RemoveQuestion(QuestionId id)
    {
        if (!IsDraft)
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (questions.RemoveAll(question => question.Id == id) == 0)
        {
            return Failure(CourseVersionErrors.QuestionNotFound(Id, id));
        }

        return Success(Unit.Value);
    }

    /// <summary>An editor signs off a question: recorded with who and when, shown to auditors (story E7).</summary>
    public Result<Unit, DomainError> ApproveQuestion(QuestionId id, PersonId editor, Timestamp now)
    {
        if (Status is not (CourseVersionStatus.Draft or CourseVersionStatus.InReview))
        {
            return Refused(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (questions.SingleOrDefault(question => question.Id == id) is not { } question)
        {
            return Failure(CourseVersionErrors.QuestionNotFound(Id, id));
        }

        if (question.Review == ReviewState.Approved)
        {
            return Failure(CourseVersionErrors.QuestionAlreadyApproved(Id, id));
        }

        if (question.Defect(id) is { HasValue: true } defect)
        {
            return Refused(defect.Value);
        }

        question.Approve(editor, now);
        return Success(Unit.Value);
    }

    // ── Workflow ──

    public Result<Unit, CourseVersionWorkflowError> SubmitForReview(PersonId editor)
    {
        if (!IsDraft)
        {
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (chapters.Count == 0)
        {
            return Failure(CourseVersionErrors.NoChapters(Id));
        }

        if (questions.Count > QuestionCount.Maximum)
        {
            return Failure(CourseVersionErrors.TooManyQuestions(Id));
        }

        if (questions.Count != Blueprint.Questions.Value)
        {
            return Failure(CourseVersionErrors.TestIncomplete(Id, Blueprint.Questions, QuestionCount.From(questions.Count)));
        }

        SubmittedBy = editor;
        Status = CourseVersionStatus.InReview;
        Raise(new SubmittedForReview(Id, editor));
        return Success(Unit.Value);
    }

    /// <summary>Sends the version back to its editors; records who did and when, and a later return overwrites both.</summary>
    public Result<Unit, CourseVersionWorkflowError> ReturnToDraft(PersonId editor, Timestamp now)
    {
        if (Status != CourseVersionStatus.InReview)
        {
            return Failure(CourseVersionErrors.NotInReview(Id, Status));
        }

        Status = CourseVersionStatus.Draft;
        ReturnedBy = editor;
        ReturnedAt = now;
        Raise(new CourseVersionReturned(Id, editor));
        return Success(Unit.Value);
    }

    /// <summary>
    /// Publishes the version. Every question is approved and sound, and with <see cref="ReviewPolicy.FourEyes"/> the
    /// publishing editor is neither the one who submitted it nor one who approved any of its questions, a question copied
    /// from the previous version included. The Application supersedes the course's previous version.
    /// </summary>
    public Result<Unit, CourseVersionWorkflowError> Publish(PersonId editor, ReviewPolicy policy, Timestamp now)
    {
        if (Status != CourseVersionStatus.InReview)
        {
            return Failure(CourseVersionErrors.NotInReview(Id, Status));
        }

        // Checked here too, not left to SubmitForReview: a version loaded in review may hold rows its rules never took.
        if (questions.Count > QuestionCount.Maximum)
        {
            return Failure(CourseVersionErrors.TooManyQuestions(Id));
        }

        if (questions.Count != Blueprint.Questions.Value)
        {
            return Failure(CourseVersionErrors.TestIncomplete(Id, Blueprint.Questions, QuestionCount.From(questions.Count)));
        }

        if (policy == ReviewPolicy.FourEyes && editor == SubmittedBy)
        {
            return Failure(CourseVersionErrors.FourEyesRequired(Id));
        }

        if (policy == ReviewPolicy.FourEyes && questions.Any(question => question.ReviewedBy == editor))
        {
            return Failure(CourseVersionErrors.PublisherApprovedQuestions(Id));
        }

        if (questions.FirstOrDefault(question => question.Review != ReviewState.Approved) is { } unreviewed)
        {
            return Failure(CourseVersionErrors.QuestionNotApproved(Id, unreviewed.Id));
        }

        foreach (var question in questions)
        {
            if (question.Defect(question.Id) is { HasValue: true } defect)
            {
                return Failure(defect.Value);
            }
        }

        Status = CourseVersionStatus.Published;
        PublishedBy = editor;
        PublishedAt = now;
        Raise(new CourseVersionPublished(Id, Course, Number, now));
        return Success(Unit.Value);
    }

    /// <summary>A newer version was published. Running sessions on this one stay valid.</summary>
    public void Supersede()
    {
        if (Status == CourseVersionStatus.Published)
        {
            Status = CourseVersionStatus.Superseded;
        }
    }

    /// <summary>Retires a published or superseded version, alone under any review policy, and records who did and when.</summary>
    public Result<Unit, CourseVersionWorkflowError> Retire(PersonId editor, Timestamp now)
    {
        if (Status is not (CourseVersionStatus.Published or CourseVersionStatus.Superseded))
        {
            return Failure(CourseVersionErrors.NotPublished(Id, Status));
        }

        Status = CourseVersionStatus.Retired;
        RetiredBy = editor;
        RetiredAt = now;
        Raise(new CourseVersionRetired(Id, Course, editor));
        return Success(Unit.Value);
    }

    /// <summary>
    /// Appends questions as EF Core loads them, past every rule: rows written before a rule existed, or by hand. Tests
    /// stand in for such a load with it.
    /// </summary>
    internal void LoadQuestions(IEnumerable<Question> loaded)
    {
        Ensure.That(loaded).IsNotNull();

        questions.AddRange(loaded);
    }

    /// <summary>A refusal as the <see cref="DomainError"/> a method outside the workflow returns, a workflow error included.</summary>
    private static FailureOutcome<DomainError> Refused(DomainError error) => Failure(error);
}
