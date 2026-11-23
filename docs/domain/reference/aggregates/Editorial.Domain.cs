// Editorial service — Domain (module.style: ddd, module.ddd-depth: strict)
// One file for review; in the project every type lives in its own file under Domain/CourseVersions/.
// Identities (CourseId, CourseVersionId, ChapterId, QuestionId, AnswerOptionId, PersonId), Timestamp,
// ScorePercent and RecurrenceInterval: same shape as in the other files.

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

// ───────────────────────────── Values ─────────────────────────────

public sealed record VersionNumber : IValueObject<int>
{
    private VersionNumber(int value) => Value = value;

    public int Value { get; }

    public static VersionNumber First => new(1);

    public static VersionNumber From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new VersionNumber(value);
    }

    public VersionNumber Next() => new(Value + 1);
}

public sealed record CourseTitle : IValueObject<string>
{
    public const int MaxLength = 150;

    private CourseTitle(string value) => Value = value;

    public string Value { get; }

    public static CourseTitle From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new CourseTitle(value.Trim());
    }
}

public sealed record ChapterTitle : IValueObject<string>
{
    public const int MaxLength = 150;

    private ChapterTitle(string value) => Value = value;

    public string Value { get; }

    public static ChapterTitle From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new ChapterTitle(value.Trim());
    }
}

/// <summary>The body of a text chapter, written in Markdown.</summary>
public sealed record MarkdownText : IValueObject<string>
{
    public const int MaxLength = 200_000;

    private MarkdownText(string value) => Value = value;

    public string Value { get; }

    public static MarkdownText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new MarkdownText(value);
    }
}

/// <summary>Where a PDF or video lies in blob storage: the container-relative name, never a URL with a key.</summary>
public sealed record BlobReference : IValueObject<string>
{
    public const int MaxLength = 1024;

    private BlobReference(string value) => Value = value;

    public string Value { get; }

    public static BlobReference From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(v => !v.Contains("://", StringComparison.Ordinal), "Eine Blob-Referenz ist ein Name, keine URL.");
        return new BlobReference(value);
    }
}

/// <summary>A video's length in whole seconds; Learning uses it to judge whether progress is plausible.</summary>
public sealed record VideoDuration : IValueObject<int>
{
    private VideoDuration(int value) => Value = value;

    public int Value { get; }

    public static VideoDuration From(int seconds)
    {
        Ensure.That(seconds).InRange(1, 4 * 60 * 60);
        return new VideoDuration(seconds);
    }
}

public sealed record QuestionText : IValueObject<string>
{
    public const int MaxLength = 1000;

    private QuestionText(string value) => Value = value;

    public string Value { get; }

    public static QuestionText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new QuestionText(value.Trim());
    }
}

public sealed record AnswerText : IValueObject<string>
{
    public const int MaxLength = 400;

    private AnswerText(string value) => Value = value;

    public string Value { get; }

    public static AnswerText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new AnswerText(value.Trim());
    }
}

public sealed record QuestionCount : IValueObject<int>
{
    private QuestionCount(int value) => Value = value;

    public int Value { get; }

    public static QuestionCount Default => new(10);

    public static QuestionCount From(int value)
    {
        Ensure.That(value).InRange(0, 50);
        return new QuestionCount(value);
    }
}

/// <summary>What the test of this version must look like; the AI draft is made to it and Publish checks against it.</summary>
public sealed record TestBlueprint
{
    public TestBlueprint(QuestionCount questions, ScorePercent passingScore, RecurrenceInterval validity)
    {
        Ensure.That(questions.Value).AtLeast(1);
        Questions = questions;
        PassingScore = passingScore;
        Validity = validity;
    }

    public QuestionCount Questions { get; }

    public ScorePercent PassingScore { get; }

    /// <summary>How long a certificate for this version is valid, and how often a recurring assignment repeats.</summary>
    public RecurrenceInterval Validity { get; }

    public static TestBlueprint Default => new(QuestionCount.Default, ScorePercent.From(80), RecurrenceInterval.Yearly);
}

public enum ChapterKind
{
    Text,
    Pdf,
    Video,
}

public enum QuestionType
{
    SingleChoice,
    MultipleChoice,
}

public enum Correctness
{
    Correct,
    Incorrect,
}

/// <summary>Where a question came from. An AI-drafted question stays AiDrafted after a human edits it.</summary>
public enum Provenance
{
    Human,
    AiDrafted,
}

public enum ReviewState
{
    Unreviewed,
    Approved,
}

public enum CourseVersionStatus
{
    Draft,
    InReview,
    Published,
    Superseded,
    Retired,
}

/// <summary>Set per platform by the editorial team; read by the Application and handed to Publish.</summary>
public enum ReviewPolicy
{
    SingleEditor,
    FourEyes,
}

// ───────────────────────────── Entities ─────────────────────────────

public sealed class Chapter : Entity<ChapterId>
{
    private Chapter(ChapterId id, ChapterTitle title, ChapterKind kind)
        : base(id)
    {
        Title = title;
        Kind = kind;
    }

    public ChapterTitle Title { get; private set; }

    public ChapterKind Kind { get; private set; }

    public MarkdownText? Text { get; private set; }

    public BlobReference? File { get; private set; }

    public VideoDuration? Duration { get; private set; }

    public static Chapter OfText(ChapterId id, ChapterTitle title, MarkdownText text) => new(id, title, ChapterKind.Text) { Text = text };

    public static Chapter OfPdf(ChapterId id, ChapterTitle title, BlobReference pdf) => new(id, title, ChapterKind.Pdf) { File = pdf };

    public static Chapter OfVideo(ChapterId id, ChapterTitle title, BlobReference video, VideoDuration duration) =>
        new(id, title, ChapterKind.Video) { File = video, Duration = duration };

    internal Chapter CopyFor(ChapterId id) => new(id, Title, Kind) { Text = Text, File = File, Duration = Duration };
}

public sealed class AnswerOption : Entity<AnswerOptionId>
{
    internal AnswerOption(AnswerOptionId id, AnswerText text, Correctness correctness)
        : base(id)
    {
        Text = text;
        Correctness = correctness;
    }

    public AnswerText Text { get; }

    public Correctness Correctness { get; }
}

/// <summary>What the LLM or an editor hands in for one question, before it becomes part of the version.</summary>
public sealed record DraftedQuestion(
    QuestionText Text,
    QuestionType Type,
    ChapterId Chapter,
    IReadOnlyList<(AnswerText Text, Correctness Correctness)> Options);

public sealed class Question : Entity<QuestionId>
{
    private readonly List<AnswerOption> options = [];

    private Question(QuestionId id, QuestionText text, QuestionType type, ChapterId chapter, Provenance provenance)
        : base(id)
    {
        Text = text;
        Type = type;
        Chapter = chapter;
        Provenance = provenance;
        Review = ReviewState.Unreviewed;
    }

    public QuestionText Text { get; private set; }

    public QuestionType Type { get; private set; }

    public ChapterId Chapter { get; private set; }

    public IReadOnlyList<AnswerOption> Options => options;

    public Provenance Provenance { get; }

    public ReviewState Review { get; private set; }

    public PersonId? ReviewedBy { get; private set; }

    public Timestamp? ReviewedAt { get; private set; }

    internal static Question From(QuestionId id, DraftedQuestion draft, Provenance provenance, Func<AnswerOptionId> newOptionId)
    {
        var question = new Question(id, draft.Text, draft.Type, draft.Chapter, provenance);
        question.ReplaceOptions(draft.Options, newOptionId);
        return question;
    }

    internal void Edit(DraftedQuestion draft, Func<AnswerOptionId> newOptionId)
    {
        Text = draft.Text;
        Type = draft.Type;
        Chapter = draft.Chapter;
        ReplaceOptions(draft.Options, newOptionId);
        MarkUnreviewed();
    }

    internal void Approve(PersonId editor, Timestamp now)
    {
        Review = ReviewState.Approved;
        ReviewedBy = editor;
        ReviewedAt = now;
    }

    internal void MarkUnreviewed()
    {
        Review = ReviewState.Unreviewed;
        ReviewedBy = null;
        ReviewedAt = null;
    }

    /// <summary>Single choice: exactly one correct option. Multiple choice: at least one. Always at least two options.</summary>
    internal Option<CourseVersionWorkflowError> Defect(QuestionId id)
    {
        var correct = options.Count(option => option.Correctness == Correctness.Correct);
        return options.Count < 2 ? Option<CourseVersionWorkflowError>.Some(new TooFewOptions(id))
            : Type == QuestionType.SingleChoice && correct != 1 ? Option<CourseVersionWorkflowError>.Some(new SingleChoiceNeedsOneCorrect(id))
            : Type == QuestionType.MultipleChoice && correct == 0 ? Option<CourseVersionWorkflowError>.Some(new MultipleChoiceNeedsACorrect(id))
            : Option<CourseVersionWorkflowError>.None;
    }

    internal Question CopyFor(QuestionId id, ChapterId chapter, Func<AnswerOptionId> newOptionId)
    {
        var copy = new Question(id, Text, Type, chapter, Provenance) { Review = Review, ReviewedBy = ReviewedBy, ReviewedAt = ReviewedAt };
        copy.ReplaceOptions([.. options.Select(option => (option.Text, option.Correctness))], newOptionId);
        return copy;
    }

    private void ReplaceOptions(IReadOnlyList<(AnswerText Text, Correctness Correctness)> drafted, Func<AnswerOptionId> newOptionId)
    {
        options.Clear();
        options.AddRange(drafted.Select(option => new AnswerOption(newOptionId(), option.Text, option.Correctness)));
    }
}

// ───────────────────────────── Aggregate ─────────────────────────────

/// <summary>
/// One version of a course, from the first draft to its retirement. Once published it never changes: every change
/// after that is a new version, started with <see cref="DraftNext"/>.
/// </summary>
public sealed class CourseVersion : AggregateRoot<CourseVersionId>
{
    private readonly List<Chapter> chapters = [];
    private readonly List<Question> questions = [];

    private CourseVersion(CourseVersionId id, CourseId course, VersionNumber number, CourseTitle title, TestBlueprint blueprint, PersonId createdBy)
        : base(id)
    {
        Course = course;
        Number = number;
        Title = title;
        Blueprint = blueprint;
        CreatedBy = createdBy;
        Status = CourseVersionStatus.Draft;
    }

    public CourseId Course { get; }

    public VersionNumber Number { get; }

    public CourseTitle Title { get; private set; }

    public TestBlueprint Blueprint { get; private set; }

    public CourseVersionStatus Status { get; private set; }

    public IReadOnlyList<Chapter> Chapters => chapters;

    /// <summary>A read-only view: a cast does not lead back to the list behind it.</summary>
    public IReadOnlyList<Question> Questions => questions.AsReadOnly();

    public PersonId CreatedBy { get; }

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
        if (Status != CourseVersionStatus.Published)
        {
            return Failure(new NotPublished(Id, Status));
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

    public Result<ChapterId, DomainError> AddChapter(Chapter chapter)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        chapters.Add(chapter);
        return Success(chapter.Id);
    }

    /// <summary>Replaces a chapter's content. Its questions need a fresh review, since what they ask about changed.</summary>
    public Result<Unit, DomainError> ReplaceChapter(Chapter replacement)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        var index = chapters.FindIndex(chapter => chapter.Id == replacement.Id);
        if (index < 0)
        {
            return Failure(new ChapterNotFound(Id, replacement.Id));
        }

        chapters[index] = replacement;
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
            return Failure(new NotADraft(Id, Status));
        }

        if (chapters.RemoveAll(existing => existing.Id == chapter) == 0)
        {
            return Failure(new ChapterNotFound(Id, chapter));
        }

        questions.RemoveAll(question => question.Chapter == chapter);
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> ReorderChapters(IReadOnlyList<ChapterId> order)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        if (order.Count != chapters.Count || !order.ToHashSet().SetEquals(chapters.Select(chapter => chapter.Id)))
        {
            return Failure(new OrderDoesNotMatchChapters(Id));
        }

        var byId = chapters.ToDictionary(chapter => chapter.Id);
        chapters.Clear();
        chapters.AddRange(order.Select(id => byId[id]));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> Rename(CourseTitle title)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        Title = title;
        return Success(Unit.Value);
    }

    // ── Test ──

    public Result<Unit, DomainError> SetBlueprint(TestBlueprint blueprint)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
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
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        if (drafted.FirstOrDefault(draft => chapters.All(chapter => chapter.Id != draft.Chapter)) is { } stray)
        {
            return Failure(new ChapterNotFound(Id, stray.Chapter));
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
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        if (chapters.All(chapter => chapter.Id != draft.Chapter))
        {
            return Failure(new ChapterNotFound(Id, draft.Chapter));
        }

        questions.Add(Question.From(id, draft, Provenance.Human, newOptionId));
        return Success(id);
    }

    public Result<Unit, DomainError> EditQuestion(QuestionId id, DraftedQuestion draft, Func<AnswerOptionId> newOptionId)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        if (questions.SingleOrDefault(question => question.Id == id) is not { } question)
        {
            return Failure(new QuestionNotFound(Id, id));
        }

        question.Edit(draft, newOptionId);
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> RemoveQuestion(QuestionId id)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        return questions.RemoveAll(question => question.Id == id) == 0
            ? Failure(new QuestionNotFound(Id, id))
            : Success(Unit.Value);
    }

    /// <summary>An editor signs off a question: recorded with who and when, shown to auditors (story E7).</summary>
    public Result<Unit, DomainError> ApproveQuestion(QuestionId id, PersonId editor, Timestamp now)
    {
        if (Status is not (CourseVersionStatus.Draft or CourseVersionStatus.InReview))
        {
            return Failure(new NotADraft(Id, Status));
        }

        if (questions.SingleOrDefault(question => question.Id == id) is not { } question)
        {
            return Failure(new QuestionNotFound(Id, id));
        }

        if (question.Review == ReviewState.Approved)
        {
            return Failure(new QuestionAlreadyApproved(Id, id));
        }

        // Every case of the workflow union is a DomainError, so the pattern always matches a defect.
        if (question.Defect(id) is { HasValue: true } defect && defect.Value.Value is DomainError refusal)
        {
            return Failure(refusal);
        }

        question.Approve(editor, now);
        return Success(Unit.Value);
    }

    // ── Workflow ──

    public Result<Unit, CourseVersionWorkflowError> SubmitForReview(PersonId editor)
    {
        if (!IsDraft)
        {
            return Failure(new NotADraft(Id, Status));
        }

        if (chapters.Count == 0)
        {
            return Failure(new NoChapters(Id));
        }

        if (questions.Count > QuestionCount.Maximum)
        {
            return Failure(new TooManyQuestions(Id));
        }

        if (questions.Count != Blueprint.Questions.Value)
        {
            return Failure(new TestIncomplete(Id, Blueprint.Questions, QuestionCount.From(questions.Count)));
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
            return Failure(new NotInReview(Id, Status));
        }

        Status = CourseVersionStatus.Draft;
        ReturnedBy = editor;
        ReturnedAt = now;
        Raise(new CourseVersionReturned(Id, editor));
        return Success(Unit.Value);
    }

    /// <summary>
    /// Publishes the version. Every question is approved and sound, and with <see cref="ReviewPolicy.FourEyes"/> the
    /// publishing editor is neither the one who submitted it nor one who approved any of its questions. The question count
    /// is checked again, since a version loaded in review may hold rows its rules never took. The Application supersedes
    /// the course's previous version.
    /// </summary>
    public Result<Unit, CourseVersionWorkflowError> Publish(PersonId editor, ReviewPolicy policy, Timestamp now)
    {
        if (Status != CourseVersionStatus.InReview)
        {
            return Failure(new NotInReview(Id, Status));
        }

        if (questions.Count > QuestionCount.Maximum)
        {
            return Failure(new TooManyQuestions(Id));
        }

        if (questions.Count != Blueprint.Questions.Value)
        {
            return Failure(new TestIncomplete(Id, Blueprint.Questions, QuestionCount.From(questions.Count)));
        }

        if (policy == ReviewPolicy.FourEyes && editor == SubmittedBy)
        {
            return Failure(new FourEyesRequired(Id));
        }

        if (policy == ReviewPolicy.FourEyes && questions.Any(question => question.ReviewedBy == editor))
        {
            return Failure(new PublisherApprovedQuestions(Id));
        }

        if (questions.FirstOrDefault(question => question.Review != ReviewState.Approved) is { } unreviewed)
        {
            return Failure(new QuestionNotApproved(Id, unreviewed.Id));
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
            return Failure(new NotPublished(Id, Status));
        }

        Status = CourseVersionStatus.Retired;
        RetiredBy = editor;
        RetiredAt = now;
        Raise(new CourseVersionRetired(Id, Course, editor));
        return Success(Unit.Value);
    }

    /// <summary>Appends questions past every rule: an internal seam for tests to reach a version no route can produce.</summary>
    internal void LoadQuestions(IEnumerable<Question> loaded) => questions.AddRange(loaded);
}

// ───────────────────────────── Domain events ─────────────────────────────
// CourseVersionPublished → integration events CourseVersionPublishedV1 (content, without questions) and
// TestDefinitionPublishedV1 (questions with correct answers, for Assessment only), built by the Application.

public sealed record CourseVersionPublished(CourseVersionId Version, CourseId Course, VersionNumber Number, Timestamp PublishedAt) : IDomainEvent;

public sealed record CourseVersionRetired(CourseVersionId Version, CourseId Course, PersonId RetiredBy) : IDomainEvent;

[DeliberatelyUnhandled("Audit only: the version records who returned it and when.")]
public sealed record CourseVersionReturned(CourseVersionId Version, PersonId ReturnedBy) : IDomainEvent;

[DeliberatelyUnhandled("Shown in the editorial inbox through a query; no reaction needed.")]
public sealed record SubmittedForReview(CourseVersionId Version, PersonId SubmittedBy) : IDomainEvent;

[DeliberatelyUnhandled("Audit only.")]
public sealed record AiTestDraftApplied(CourseVersionId Version, QuestionCount Drafted) : IDomainEvent;

// ───────────────────────────── Domain errors ─────────────────────────────
// What a step of the workflow (SubmitForReview, ReturnToDraft, Publish, Retire) refuses is a closed union, declared
// with the union keyword: only a listed record converts to it, it holds its case in Value, and a switch on it without
// a discard arm fails the build until a new case is mapped (CourseVersionFailures.From). Each case is a DomainError
// of its own, so the version's other methods return it too.

public union CourseVersionWorkflowError(NotADraft, NotInReview, NotPublished, NoChapters, TooManyQuestions, TestIncomplete, QuestionNotApproved, FourEyesRequired, PublisherApprovedQuestions, TooFewOptions, SingleChoiceNeedsOneCorrect, MultipleChoiceNeedsACorrect);

public sealed record NotADraft(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-a-draft", "Nur ein Entwurf kann bearbeitet werden.");

public sealed record NotInReview(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-in-review", "Die Version ist nicht in Prüfung.");

public sealed record NotPublished(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-published", "Die Version ist nicht veröffentlicht.");

public sealed record ChapterNotFound(CourseVersionId Version, ChapterId Chapter)
    : DomainError("editorial.chapter-not-found", "Dieses Kapitel gibt es in der Version nicht.");

public sealed record OrderDoesNotMatchChapters(CourseVersionId Version)
    : DomainError("editorial.order-does-not-match", "Die Reihenfolge muss jedes Kapitel genau einmal enthalten.");

public sealed record QuestionNotFound(CourseVersionId Version, QuestionId Question)
    : DomainError("editorial.question-not-found", "Diese Frage gibt es in der Version nicht.");

public sealed record NoChapters(CourseVersionId Version)
    : DomainError("editorial.no-chapters", "Eine Schulung braucht mindestens ein Kapitel.");

public sealed record TestIncomplete(CourseVersionId Version, QuestionCount Expected, QuestionCount Actual)
    : DomainError("editorial.test-incomplete", $"Der Test braucht {Expected.Value} Fragen.");

public sealed record QuestionNotApproved(CourseVersionId Version, QuestionId Question)
    : DomainError("editorial.question-not-approved", "Jede Frage muss vor der Veröffentlichung freigegeben sein.");

public sealed record FourEyesRequired(CourseVersionId Version)
    : DomainError("editorial.four-eyes", "Veröffentlichen muss eine andere Person als die, die eingereicht hat.");

public sealed record TooFewOptions(QuestionId Question)
    : DomainError("editorial.too-few-options", "Eine Frage braucht mindestens zwei Antwortoptionen.");

public sealed record SingleChoiceNeedsOneCorrect(QuestionId Question)
    : DomainError("editorial.single-choice-one-correct", "Eine Single-Choice-Frage hat genau eine richtige Antwort.");

public sealed record MultipleChoiceNeedsACorrect(QuestionId Question)
    : DomainError("editorial.multiple-choice-a-correct", "Eine Multiple-Choice-Frage hat mindestens eine richtige Antwort.");

public sealed record TooManyQuestions(CourseVersionId Version)
    : DomainError("editorial.too-many-questions", $"A test has at most {QuestionCount.Maximum} questions.");

public sealed record PublisherApprovedQuestions(CourseVersionId Version)
    : DomainError("editorial.publisher-approved-questions", "Under four eyes, an editor who approved a question of this version cannot publish it.");

public sealed record QuestionAlreadyApproved(CourseVersionId Version, QuestionId Question)
    : DomainError("editorial.question-already-approved", "This question is already approved; edit it to have it approved again.");
