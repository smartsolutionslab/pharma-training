namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// What the LLM or an editor hands in for one question, before it becomes part of the version: at most
/// <see cref="MaxOptions"/> answer options, so one question cannot make the version too large to load on every request.
/// Too few options or the wrong correct ones are a defect the version reports when the question is approved.
/// </summary>
public sealed record DraftedQuestion(
    QuestionText Text,
    QuestionType Type,
    ChapterId Chapter,
    IReadOnlyList<(AnswerText Text, Correctness Correctness)> Options)
{
    public const int MaxOptions = 10;

    public IReadOnlyList<(AnswerText Text, Correctness Correctness)> Options { get; } = Guarded(Options);

    private static IReadOnlyList<(AnswerText Text, Correctness Correctness)> Guarded(IReadOnlyList<(AnswerText Text, Correctness Correctness)> options)
    {
        Ensure.That(options).IsNotNull();
        Ensure.That(options.Count).InRange(0, MaxOptions);
        return options;
    }
}
