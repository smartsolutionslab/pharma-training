namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning's <c>ContentCompleted</c> as it is stored (value objects as their values), appended under its event type
/// name to a session's stream with no marker and no envelope: the state a crash between the append and the outbox
/// commit leaves (#277). Integration.Tests references no module (AGENTS.md: no per-module ProjectReference in a shared file).
/// </summary>
internal sealed record PlantedContentCompleted(
    Guid Session,
    Guid Learner,
    Guid Course,
    Guid CourseVersion,
    DateTimeOffset CompletedAt);
