namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// The order of an attempt's questions and of each question's options, derived from the attempt's id (ADR 0022).
/// Stub for the red run of #46: not implemented yet.
/// </summary>
public static class AttemptShuffle
{
    public static IReadOnlyList<QuestionVersionId> Questions(AttemptId attempt, IEnumerable<QuestionVersionId> questions) =>
        throw new NotImplementedException();

    public static IReadOnlyList<AnswerOptionId> Options(AttemptId attempt, IEnumerable<AnswerOptionId> options) =>
        throw new NotImplementedException();
}
