using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.IntegrationEvents;

/// <summary>
/// How a course version is announced to other modules; pure, no I/O. The course version goes out with its chapters
/// and without questions; its test, with the solutions, goes out separately and to Assessment only; its retirement
/// names the course and the version.
/// </summary>
public static class EditorialIntegrationEventMappings
{
    /// <summary>
    /// How often a learner may attempt a test: "höchstens 3 Versuche", "später evtl. konfigurierbar"
    /// (<c>docs/domain/README.md</c>). Fixed for MVP 1, so a constant rather than configuration.
    /// </summary>
    public const int MaxAttempts = 3;

    public static CourseVersionPublishedV1 ToCourseVersionPublishedV1(this CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        return new CourseVersionPublishedV1(
            version.Course.Value,
            version.Id.Value,
            version.Number.Value,
            version.Title.Value,
            version.Blueprint.Validity.Value,
            [.. version.Chapters.Select((chapter, index) => new CourseChapterV1(
                chapter.Id.Value,
                index + 1,
                chapter.Title.Value,
                chapter.Kind.ToV1(),
                chapter.Duration?.Value,
                chapter.Text?.Value,
                chapter.File?.Value))]);
    }

    public static TestDefinitionPublishedV1 ToTestDefinitionPublishedV1(this CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        return new TestDefinitionPublishedV1(
            version.Course.Value,
            version.Id.Value,
            version.Blueprint.PassingScore.Value,
            MaxAttempts,
            [.. version.Questions.Select(question => new TestQuestionV1(
                question.Id.Value,
                question.Chapter.Value,
                question.Type.ToV1(),
                question.Text.Value,
                [.. question.Options.Select(option => new AnswerOptionV1(
                    option.Id.Value,
                    option.Text.Value,
                    option.Correctness == Correctness.Correct))]))]);
    }

    public static CourseVersionRetiredV1 ToCourseVersionRetiredV1(this CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        return new CourseVersionRetiredV1(version.Course.Value, version.Id.Value);
    }
}
