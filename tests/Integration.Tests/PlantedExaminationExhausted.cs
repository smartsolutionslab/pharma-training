namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Assessment's <c>ExaminationExhausted</c> as it is stored (value objects as their values), appended under its event
/// type name. Integration.Tests references no module (AGENTS.md: no per-module ProjectReference in a shared file), and
/// no endpoint can exhaust an examination before #46.
/// </summary>
internal sealed record PlantedExaminationExhausted(
    Guid Examination,
    Guid Session,
    Guid Learner,
    Guid CourseVersion,
    IReadOnlyList<Guid> FailedChapters,
    DateTimeOffset ExhaustedAt);
