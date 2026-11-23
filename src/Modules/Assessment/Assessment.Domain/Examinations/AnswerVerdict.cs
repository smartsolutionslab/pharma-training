namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// Whether a question was answered right. Each member has a fixed number, as the enums stored events hold do: append
/// a member, never reorder or renumber one.
/// </summary>
public enum AnswerVerdict
{
    Right = 0,
    Wrong = 1,
}
