namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>Whether the learner may take the test; only meaningful while in progress.</summary>
public enum TestAccess
{
    Available,
    LockedAfterFailedAttempts,
}
