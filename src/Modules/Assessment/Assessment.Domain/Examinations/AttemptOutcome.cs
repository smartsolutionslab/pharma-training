namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// How a submitted attempt ended. Stored events hold the number: append a member, never reorder or renumber one.
/// </summary>
public enum AttemptOutcome
{
    Passed = 0,
    Failed = 1,
}
