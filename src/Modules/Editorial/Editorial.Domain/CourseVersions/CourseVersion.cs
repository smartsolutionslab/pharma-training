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

    /// <summary>The editor who last returned the version; every return, with its reason, is a <see cref="CourseVersionReturn"/>.</summary>
    public PersonId? ReturnedBy { get; private set; }

    public Timestamp? ReturnedAt { get; private set; }

    public PersonId? RetiredBy { get; private set; }

    public Timestamp? RetiredAt { get; private set; }

    private bool IsDraft => Status == CourseVersionStatus.Draft;

    public static CourseVersion DraftFirst(CourseVersionId id, CourseId course, CourseTitle title, PersonId editor) =>
        new(id, course, VersionNumber.First, title, TestBlueprint.Default, editor);

    /// <summary>
    /// The next version of a published one: a copy of its chapters and questions with new ids. Approved questions
    /// stay approved until their chapter, they themselves or the test's settings change.
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
            return Failure(CourseVersionErrors.NotPublished(Id, Status));
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
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        return Success(Unit.Value);
    }

    /// <summary>
    /// Whether the file may become the chapter's, before the storage is asked about it: the version is a draft and no
    /// other chapter of it holds the file, whether an earlier add recorded it or a copy brought it from the version before.
    /// </summary>
    public Result<Unit, DomainError> AcceptsFile(ChapterId chapter, BlobReference file)
    {
        Ensure.That(file).IsNotNull();

        var accepts = AcceptsUploads();
        if (accepts.IsFailure)
        {
            return accepts;
        }

        if (IsHeldByAnotherChapter(file, chapter))
        {
            return Failure(CourseVersionErrors.FileInUse(Id, file));
        }

        return Success(Unit.Value);
    }

    public Result<ChapterId, DomainError> AddChapter(Chapter chapter)
    {
        Ensure.That(chapter).IsNotNull();

        if (!IsDraft)
        {
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (chapter.File is { } file && IsHeldByAnotherChapter(file, chapter.Id))
        {
            return Failure(CourseVersionErrors.FileInUse(Id, file));
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
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (chapters.SingleOrDefault(chapter => chapter.Id == replacement.Id) is not { } stored)
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, replacement.Id));
        }

        // The chapter's own file is no other's: a replacement that keeps it is no conflict.
        if (replacement.File is { } file && IsHeldByAnotherChapter(file, replacement.Id))
        {
            return Failure(CourseVersionErrors.FileInUse(Id, file));
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
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
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
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
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
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        Title = title;
        return Success(Unit.Value);
    }

    // ── Test ──

    /// <summary>
    /// Replaces a draft's test settings. Replacing them sends every question of the version back to review, copied
    /// ones included, so reviewers sign off the changed test again. The same settings again change nothing.
    /// </summary>
    public Result<Unit, DomainError> SetBlueprint(TestBlueprint blueprint)
    {
        Ensure.That(blueprint).IsNotNull();

        if (!IsDraft)
        {
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (blueprint == Blueprint)
        {
            return Success(Unit.Value);
        }

        Blueprint = blueprint;
        foreach (var question in questions)
        {
            question.MarkUnreviewed();
        }

        return Success(Unit.Value);
    }

    /// <summary>
    /// Takes the LLM's draft of the whole test. Only unreviewed AI-drafted questions are replaced (#245, the owner's
    /// decision of 2026-11-23): approved questions stay, and so do copied and hand-written ones even while unreviewed, so
    /// a new draft never throws away work an editor signed off or wrote, nor the test a settings change sent back to
    /// review. Answers the drafted questions' new ids, in the draft's order, after the questions it keeps.
    /// </summary>
    public Result<IReadOnlyList<QuestionId>, DomainError> ApplyAiTestDraft(IReadOnlyList<DraftedQuestion> drafted, Func<QuestionId> newQuestionId, Func<AnswerOptionId> newOptionId)
    {
        Ensure.That(drafted).IsNotNull();
        Ensure.That(newQuestionId).IsNotNull();

        if (!IsDraft)
        {
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (drafted.FirstOrDefault(draft => chapters.All(chapter => chapter.Id != draft.Chapter)) is { } stray)
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, stray.Chapter));
        }

        if (questions.Count(question => !question.IsReplacedByAiDraft()) + drafted.Count > QuestionCount.Maximum)
        {
            return Failure(CourseVersionErrors.TooManyQuestions(Id));
        }

        List<Question> added = [.. drafted.Select(draft => Question.DraftedByAi(newQuestionId(), draft, newOptionId))];
        questions.RemoveAll(question => question.IsReplacedByAiDraft());
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
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (chapters.All(chapter => chapter.Id != draft.Chapter))
        {
            return Failure(CourseVersionErrors.ChapterNotFound(Id, draft.Chapter));
        }

        if (questions.Count >= QuestionCount.Maximum)
        {
            return Failure(CourseVersionErrors.TooManyQuestions(Id));
        }

        questions.Add(Question.WrittenByHand(id, draft, newOptionId));
        return Success(id);
    }

    public Result<Unit, DomainError> EditQuestion(QuestionId id, DraftedQuestion draft, Func<AnswerOptionId> newOptionId)
    {
        Ensure.That(draft).IsNotNull();

        if (!IsDraft)
        {
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
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
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (questions.RemoveAll(question => question.Id == id) == 0)
        {
            return Failure(CourseVersionErrors.QuestionNotFound(Id, id));
        }

        return Success(Unit.Value);
    }

    /// <summary>An editor signs off a question: recorded with who, under which display name, and when, shown to auditors (story E7, #205).</summary>
    public Result<Unit, DomainError> ApproveQuestion(QuestionId id, PersonId editor, ReviewerName name, Timestamp now)
    {
        if (Status is not (CourseVersionStatus.Draft or CourseVersionStatus.InReview))
        {
            return Failure(CourseVersionErrors.NotADraft(Id, Status));
        }

        if (questions.SingleOrDefault(question => question.Id == id) is not { } question)
        {
            return Failure(CourseVersionErrors.QuestionNotFound(Id, id));
        }

        if (question.Review == ReviewState.Approved)
        {
            return Failure(CourseVersionErrors.QuestionAlreadyApproved(Id, id));
        }

        // Every case of the workflow union is a DomainError, so the pattern always matches a defect.
        if (question.Defect(id) is { HasValue: true } defect && defect.Value.Value is DomainError refusal)
        {
            return Failure(refusal);
        }

        question.Approve(editor, name, now);
        return Success(Unit.Value);
    }

    // ── Workflow ──

    public Result<Unit, CourseVersionWorkflowError> SubmitForReview(PersonId editor)
    {
        if (!IsDraft)
        {
            return Failure(CourseVersionErrors.Workflow.NotADraft(Id, Status));
        }

        if (chapters.Count == 0)
        {
            return Failure(CourseVersionErrors.Workflow.NoChapters(Id));
        }

        if (questions.Count > QuestionCount.Maximum)
        {
            return Failure(CourseVersionErrors.Workflow.TooManyQuestions(Id));
        }

        if (questions.Count != Blueprint.Questions.Value)
        {
            return Failure(CourseVersionErrors.Workflow.TestIncomplete(Id, Blueprint.Questions, QuestionCount.From(questions.Count)));
        }

        SubmittedBy = editor;
        Status = CourseVersionStatus.InReview;
        Raise(new SubmittedForReview(Id, editor));
        return Success(Unit.Value);
    }

    /// <summary>
    /// Sends the version back to its editors and hands back the record of who did, under which name, when and why, which
    /// the caller stores in the same save; the version keeps the latest editor and time. Returning is not bound by four
    /// eyes.
    /// </summary>
    public Result<CourseVersionReturn, CourseVersionWorkflowError> ReturnToDraft(PersonId editor, ReviewerName name, ReturnReason reason, Timestamp now)
    {
        Ensure.That(name).IsNotNull();
        Ensure.That(reason).IsNotNull();

        if (Status != CourseVersionStatus.InReview)
        {
            return Failure(CourseVersionErrors.Workflow.NotInReview(Id, Status));
        }

        Status = CourseVersionStatus.Draft;
        ReturnedBy = editor;
        ReturnedAt = now;
        Raise(new CourseVersionReturned(Id, editor, reason));
        return Success(new CourseVersionReturn(CourseVersionReturnId.New(), Id, editor, name, now, reason));
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
            return Failure(CourseVersionErrors.Workflow.NotInReview(Id, Status));
        }

        // Checked here too, not left to SubmitForReview: a version loaded in review may hold rows its rules never took.
        if (questions.Count > QuestionCount.Maximum)
        {
            return Failure(CourseVersionErrors.Workflow.TooManyQuestions(Id));
        }

        if (questions.Count != Blueprint.Questions.Value)
        {
            return Failure(CourseVersionErrors.Workflow.TestIncomplete(Id, Blueprint.Questions, QuestionCount.From(questions.Count)));
        }

        if (policy == ReviewPolicy.FourEyes && editor == SubmittedBy)
        {
            return Failure(CourseVersionErrors.Workflow.FourEyesRequired(Id));
        }

        if (policy == ReviewPolicy.FourEyes && questions.Any(question => question.ReviewedBy == editor))
        {
            return Failure(CourseVersionErrors.Workflow.PublisherApprovedQuestions(Id));
        }

        if (questions.FirstOrDefault(question => question.Review != ReviewState.Approved) is { } unreviewed)
        {
            return Failure(CourseVersionErrors.Workflow.QuestionNotApproved(Id, unreviewed.Id));
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
            return Failure(CourseVersionErrors.Workflow.NotPublished(Id, Status));
        }

        Status = CourseVersionStatus.Retired;
        RetiredBy = editor;
        RetiredAt = now;
        Raise(new CourseVersionRetired(Id, Course, editor));
        return Success(Unit.Value);
    }

    /// <summary>
    /// Appends questions past every rule. No route and no load calls it: it is an internal seam that lets tests reach a
    /// version no route can produce, such as one holding more questions than the limit or a question that breaks its shape.
    /// </summary>
    internal void LoadQuestions(IEnumerable<Question> loaded)
    {
        Ensure.That(loaded).IsNotNull();

        questions.AddRange(loaded);
    }

    /// <summary>Whether a chapter of the version other than <paramref name="chapter"/> holds the file: one file, one chapter.</summary>
    private bool IsHeldByAnotherChapter(BlobReference file, ChapterId chapter) =>
        chapters.Any(other => other.Id != chapter && other.File == file);
}
