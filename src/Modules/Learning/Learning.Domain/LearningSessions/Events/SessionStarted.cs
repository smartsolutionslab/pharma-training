using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

/// <summary>A learner started a session on a course version, with its chapters by kind. Becomes LearningSessionStartedV1 (#40).</summary>
public sealed record SessionStarted(
    LearningSessionId Session,
    PersonId Learner,
    CourseId Course,
    CourseVersionId CourseVersion,
    IReadOnlyList<ChapterId> TextChapters,
    IReadOnlyList<ChapterId> PdfChapters,
    IReadOnlyList<ChapterId> VideoChapters,
    Timestamp StartedAt) : IDomainEvent;
