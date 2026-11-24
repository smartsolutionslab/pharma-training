using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>A copy of a version's test becomes the definition the aggregate scores against, its questions and options in position order. For scoring only.</summary>
public static class TestDefinitionCopyMappings
{
    public static TestDefinition ToDefinition(this TestDefinitionCopy copy)
    {
        Ensure.That(copy).IsNotNull();

        return new TestDefinition(
            CourseVersionId.From(copy.CourseVersionId),
            [.. copy.Questions.OrderBy(question => question.Position).Select(ToDefinedQuestion)],
            ScorePercent.From(copy.PassingScorePercent),
            AttemptNumber.From(copy.MaxAttempts));
    }

    private static DefinedQuestion ToDefinedQuestion(TestQuestionCopy question)
    {
        List<TestOptionCopy> options = [.. question.Options.OrderBy(option => option.Position)];
        return new DefinedQuestion(
            QuestionVersionId.From(question.QuestionId),
            ChapterId.From(question.ChapterId),
            [.. options.Select(option => AnswerOptionId.From(option.OptionId))],
            [.. options.Where(option => option.IsCorrect).Select(option => AnswerOptionId.From(option.OptionId))]);
    }
}
