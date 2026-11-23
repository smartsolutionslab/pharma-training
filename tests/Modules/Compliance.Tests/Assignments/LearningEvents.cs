using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// Learning's and Assessment's events about a learner's progress as they arrive: published by their unit of work, so
/// each carries its metadata, and its instant is when it happened. Pass the metadata of an event sent before to have
/// the same event again.
/// </summary>
internal static class LearningEvents
{
    public static LearningSessionStartedV1 Started(Guid personId, Guid courseId, DateTimeOffset occurredAt) =>
        Started(personId, courseId, NewMetadata(occurredAt));

    public static LearningSessionStartedV1 Started(Guid personId, Guid courseId, EventMetadata metadata) =>
        new(Guid.CreateVersion7(), personId, courseId, Guid.CreateVersion7()) { Metadata = metadata };

    public static ContentCompletedV1 ContentCompleted(Guid personId, Guid courseId, DateTimeOffset occurredAt) =>
        ContentCompleted(personId, courseId, NewMetadata(occurredAt));

    public static ContentCompletedV1 ContentCompleted(Guid personId, Guid courseId, EventMetadata metadata) =>
        new(Guid.CreateVersion7(), personId, courseId, Guid.CreateVersion7()) { Metadata = metadata };

    public static AttemptsExhaustedV1 Exhausted(Guid personId, Guid courseId, DateTimeOffset occurredAt) =>
        Exhausted(personId, courseId, NewMetadata(occurredAt));

    public static AttemptsExhaustedV1 Exhausted(Guid personId, Guid courseId, EventMetadata metadata) =>
        new(Guid.CreateVersion7(), personId, courseId, Guid.CreateVersion7(), [Guid.CreateVersion7()]) { Metadata = metadata };

    private static EventMetadata NewMetadata(DateTimeOffset occurredAt) => new(Guid.CreateVersion7(), occurredAt);
}
