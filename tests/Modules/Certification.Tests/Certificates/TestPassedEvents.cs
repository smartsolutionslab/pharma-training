using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>Assessment's passed tests as they arrive: published by its unit of work, so each carries its metadata.</summary>
internal static class TestPassedEvents
{
    /// <summary>23:30 UTC on 1 March 2027, already 2 March in Germany.</summary>
    public static readonly DateTimeOffset PassedAt = new(2027, 3, 1, 23, 30, 0, TimeSpan.Zero);

    public static TestPassedV1 Passed(Guid personId, Guid courseId, Guid courseVersionId) =>
        Passed(Guid.CreateVersion7(), personId, courseId, courseVersionId, 90);

    public static TestPassedV1 Passed(Guid attemptId, Guid personId, Guid courseId, Guid courseVersionId, int scorePercent) =>
        new(attemptId, Guid.CreateVersion7(), personId, courseId, courseVersionId, scorePercent, PassedAt) { Metadata = NewMetadata() };

    /// <summary>The same event published again by Assessment, under an identifier of its own.</summary>
    public static TestPassedV1 Republished(TestPassedV1 passed) => passed with { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), PassedAt);
}
