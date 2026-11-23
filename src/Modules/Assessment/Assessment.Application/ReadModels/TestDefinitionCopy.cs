namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

/// <summary>
/// Assessment's local copy of the test of a course version Editorial published, solutions included. Written once, by
/// the consumer of <c>TestDefinitionPublishedV1</c>, and for scoring only: what a learner sees is a read model of its
/// own, without any correctness, never this one.
/// </summary>
public sealed class TestDefinitionCopy
{
    public Guid CourseVersionId { get; set; }

    public Guid CourseId { get; set; }

    public int PassingScorePercent { get; set; }

    public int MaxAttempts { get; set; }

    public List<TestQuestionCopy> Questions { get; set; } = [];
}
