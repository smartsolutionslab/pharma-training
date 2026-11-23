using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

/// <summary>Editorial's <see cref="TestDefinitionPublishedV1"/> as it arrives: published by its unit of work, so it carries its metadata.</summary>
internal static class TestDefinitionEvents
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    public static TestDefinitionPublishedV1 Published(Guid courseId, Guid courseVersionId, int passingScorePercent, int maxAttempts, IReadOnlyList<TestQuestionV1> questions) =>
        new(courseId, courseVersionId, passingScorePercent, maxAttempts, questions) { Metadata = new EventMetadata(Guid.CreateVersion7(), PublishedAt) };

    /// <summary>A single choice question whose first option is the right one.</summary>
    public static TestQuestionV1 SingleChoice(string text) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), QuestionTypeV1.SingleChoice, text, [Option("Ja", true), Option("Nein", false)]);

    /// <summary>A multiple choice question whose first two options are right and whose third is wrong.</summary>
    public static TestQuestionV1 MultipleChoice(string text) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), QuestionTypeV1.MultipleChoice, text, [Option("Handschuhe", true), Option("Kittel", true), Option("Schmuck", false)]);

    public static AnswerOptionV1 Option(string text, bool isCorrect) => new(Guid.CreateVersion7(), text, isCorrect);
}
