namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Assessment's <c>ExaminationPassed</c> as it is stored (value objects as their values), appended under its event type
/// name to an examination's stream with no marker and no envelope: the state a crash between the append and the outbox
/// commit leaves (#277). Integration.Tests references no module (AGENTS.md: no per-module ProjectReference in a shared file).
/// </summary>
internal sealed record PlantedExaminationPassed(
    Guid Examination,
    Guid Session,
    Guid Learner,
    Guid Course,
    Guid CourseVersion,
    Guid Attempt,
    int Score,
    DateTimeOffset PassedAt);
