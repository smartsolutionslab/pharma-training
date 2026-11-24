using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A test question about one chapter, with its answer options and whether an editor has signed it off.</summary>
public sealed class Question : Entity<QuestionId>
{
    private readonly List<AnswerOption> options = [];

    private Question(QuestionId id, QuestionText text, QuestionType type, ChapterId chapter, Provenance provenance, QuestionOrigin origin)
        : base(id)
    {
        Text = text;
        Type = type;
        Chapter = chapter;
        Provenance = provenance;
        Origin = origin;
        Review = ReviewState.Unreviewed;
    }

    public QuestionText Text { get; private set; }

    public QuestionType Type { get; private set; }

    public ChapterId Chapter { get; private set; }

    public IReadOnlyList<AnswerOption> Options => options;

    public Provenance Provenance { get; }

    /// <summary>How the question came into this version (#245). An editor's change keeps it, as it keeps <see cref="Provenance"/>.</summary>
    public QuestionOrigin Origin { get; }

    public ReviewState Review { get; private set; }

    public PersonId? ReviewedBy { get; private set; }

    public Timestamp? ReviewedAt { get; private set; }

    /// <summary>The approver's display name as the token carried it at approval time; null while unreviewed and for approvals recorded before #205.</summary>
    public ReviewerName? ReviewedByName { get; private set; }

    /// <summary>A question an editor writes: a person's, by hand.</summary>
    internal static Question WrittenByHand(QuestionId id, DraftedQuestion draft, Func<AnswerOptionId> newOptionId) =>
        From(id, draft, Provenance.Human, QuestionOrigin.HandWritten, newOptionId);

    /// <summary>A question the AI drafted: AI-drafted for good, and replaced by the next draft while unreviewed.</summary>
    internal static Question DraftedByAi(QuestionId id, DraftedQuestion draft, Func<AnswerOptionId> newOptionId) =>
        From(id, draft, Provenance.AiDrafted, QuestionOrigin.AiDraft, newOptionId);

    /// <summary>
    /// Whether the next AI draft replaces the question: only while it is unreviewed and the AI drafted it into this
    /// version (#245). The draft's request asks for the blueprint's count less every question this keeps.
    /// </summary>
    public bool IsReplacedByAiDraft() => Origin == QuestionOrigin.AiDraft && Review == ReviewState.Unreviewed;

    internal void Edit(DraftedQuestion draft, Func<AnswerOptionId> newOptionId)
    {
        Text = draft.Text;
        Type = draft.Type;
        Chapter = draft.Chapter;
        ReplaceOptions(draft.Options, newOptionId);
        MarkUnreviewed();
    }

    internal void Approve(PersonId editor, ReviewerName name, Timestamp now)
    {
        Review = ReviewState.Approved;
        ReviewedBy = editor;
        ReviewedByName = name;
        ReviewedAt = now;
    }

    internal void MarkUnreviewed()
    {
        Review = ReviewState.Unreviewed;
        ReviewedBy = null;
        ReviewedByName = null;
        ReviewedAt = null;
    }

    /// <summary>Single choice: exactly one correct option. Multiple choice: at least one. Always at least two options.</summary>
    internal Option<CourseVersionWorkflowError> Defect(QuestionId id)
    {
        var correct = options.Count(option => option.Correctness == Correctness.Correct);
        if (options.Count < 2)
        {
            return Option<CourseVersionWorkflowError>.Some(CourseVersionErrors.Workflow.TooFewOptions(id));
        }

        if (Type == QuestionType.SingleChoice && correct != 1)
        {
            return Option<CourseVersionWorkflowError>.Some(CourseVersionErrors.Workflow.SingleChoiceNeedsOneCorrect(id));
        }

        if (Type == QuestionType.MultipleChoice && correct == 0)
        {
            return Option<CourseVersionWorkflowError>.Some(CourseVersionErrors.Workflow.MultipleChoiceNeedsACorrect(id));
        }

        return Option<CourseVersionWorkflowError>.None;
    }

    /// <summary>The question in the next version: copied, whatever its origin here, with its provenance and its approval.</summary>
    internal Question CopyFor(QuestionId id, ChapterId chapter, Func<AnswerOptionId> newOptionId)
    {
        var copy = new Question(id, Text, Type, chapter, Provenance, QuestionOrigin.Copied) { Review = Review, ReviewedBy = ReviewedBy, ReviewedByName = ReviewedByName, ReviewedAt = ReviewedAt };
        copy.ReplaceOptions([.. options.Select(option => (option.Text, option.Correctness))], newOptionId);
        return copy;
    }

    private static Question From(QuestionId id, DraftedQuestion draft, Provenance provenance, QuestionOrigin origin, Func<AnswerOptionId> newOptionId)
    {
        var question = new Question(id, draft.Text, draft.Type, draft.Chapter, provenance, origin);
        question.ReplaceOptions(draft.Options, newOptionId);
        return question;
    }

    private void ReplaceOptions(IReadOnlyList<(AnswerText Text, Correctness Correctness)> drafted, Func<AnswerOptionId> newOptionId)
    {
        options.Clear();
        options.AddRange(drafted.Select(option => new AnswerOption(newOptionId(), option.Text, option.Correctness)));
    }
}
