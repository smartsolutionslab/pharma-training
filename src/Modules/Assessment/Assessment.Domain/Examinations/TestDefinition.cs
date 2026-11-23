namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// The test of a course version, from Assessment's local copy of <c>TestDefinitionPublished</c>, read by the
/// Application and handed to the aggregate to score an attempt. It carries the solutions: never stored in a stream and
/// never sent to the frontend.
/// </summary>
public sealed record TestDefinition(
    CourseVersionId CourseVersion,
    IReadOnlyList<DefinedQuestion> Questions,
    ScorePercent PassingScore,
    AttemptNumber MaxAttempts)
{
    /// <summary>A question counts as right only if exactly the correct options were chosen (single and multiple choice).</summary>
    public AnswerVerdict Judge(QuestionVersionId question, IReadOnlyCollection<AnswerOptionId> chosen)
    {
        Ensure.That(question).IsNotNull();
        Ensure.That(chosen).IsNotNull();

        var defined = Questions.Single(candidate => candidate.Question == question);
        return defined.CorrectOptions.ToHashSet().SetEquals(chosen) ? AnswerVerdict.Right : AnswerVerdict.Wrong;
    }

    public ChapterId ChapterOf(QuestionVersionId question) => Questions.Single(candidate => candidate.Question == question).Chapter;
}
