namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>Where a session stands: chapters still open, every chapter worked through, or the test passed.</summary>
public enum SessionStatus
{
    InProgress,
    ContentCompleted,
    Finished,
}
