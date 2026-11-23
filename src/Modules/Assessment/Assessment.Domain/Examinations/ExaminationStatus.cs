namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// Where an examination stands. Each member has a fixed number, as the enums stored events hold do: append a member,
/// never reorder or renumber one.
/// </summary>
public enum ExaminationStatus
{
    /// <summary>The content is not (yet again) worked through.</summary>
    Locked = 0,

    /// <summary>An attempt may start.</summary>
    Unlocked = 1,

    /// <summary>An attempt is open.</summary>
    InAttempt = 2,

    /// <summary>Passed; terminal.</summary>
    Passed = 3,

    /// <summary>Three failed attempts; unlocks only after the content is completed again.</summary>
    Exhausted = 4,
}
