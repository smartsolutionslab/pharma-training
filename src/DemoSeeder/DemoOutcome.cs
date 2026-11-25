namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>What a learner of a scenario does.</summary>
// stub for #457
public enum DemoOutcome
{
    /// <summary>Nothing: the assignment stays open.</summary>
    Open,

    /// <summary>Starts a session and completes its first chapter only.</summary>
    Started,

    /// <summary>Completes every chapter and passes the examination: a certificate.</summary>
    Passed,

    /// <summary>Completes every chapter and fails one attempt.</summary>
    Failed,

    /// <summary>Completes every chapter and fails every attempt, until the examination is exhausted.</summary>
    Exhausted,
}
