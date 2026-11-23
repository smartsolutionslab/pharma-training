using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

public class IdentityTests
{
    [Theory]
    [InlineData(nameof(AttemptId))]
    [InlineData(nameof(LearningSessionId))]
    [InlineData(nameof(PersonId))]
    [InlineData(nameof(CourseId))]
    [InlineData(nameof(CourseVersionId))]
    [InlineData(nameof(QuestionVersionId))]
    [InlineData(nameof(AnswerOptionId))]
    [InlineData(nameof(ChapterId))]
    public void Every_identity_refuses_an_empty_guid(string identity) =>
        Should.Throw<GuardException>(FromEmpty(identity));

    private static Action FromEmpty(string identity) =>
        identity switch
        {
            nameof(AttemptId) => () => AttemptId.From(Guid.Empty),
            nameof(LearningSessionId) => () => LearningSessionId.From(Guid.Empty),
            nameof(PersonId) => () => PersonId.From(Guid.Empty),
            nameof(CourseId) => () => CourseId.From(Guid.Empty),
            nameof(CourseVersionId) => () => CourseVersionId.From(Guid.Empty),
            nameof(QuestionVersionId) => () => QuestionVersionId.From(Guid.Empty),
            nameof(AnswerOptionId) => () => AnswerOptionId.From(Guid.Empty),
            nameof(ChapterId) => () => ChapterId.From(Guid.Empty),
            _ => throw new InvalidOperationException($"{identity} is no identity of the examination."),
        };
}
