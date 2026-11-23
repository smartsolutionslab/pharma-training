using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Consumers;

/// <summary>
/// Stores the test of a course version Editorial published with its questions, options and solutions, once: what an
/// attempt is scored against. A version is published once, so a version already stored means its event was handled,
/// whatever the event's identifier or payload: a second delivery, or a republish, changes nothing, and the first one
/// wins. The primary key backs the check: of two deliveries racing, the loser's insert fails and its retry finds the
/// row. The payload carries the solutions and is never logged.
/// </summary>
public sealed class TestDefinitionPublishedV1Consumer(ITestDefinitionCopies definitions, IAssessmentUnitOfWork unitOfWork)
{
    public async Task HandleAsync(TestDefinitionPublishedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Refuse(message);

        var version = CourseVersionId.From(message.CourseVersionId);
        if (await definitions.ExistsAsync(version, cancellationToken))
        {
            return;
        }

        definitions.Add(new TestDefinitionCopy
        {
            CourseVersionId = message.CourseVersionId,
            CourseId = message.CourseId,
            PassingScorePercent = message.PassingScorePercent,
            MaxAttempts = message.MaxAttempts,
            Questions = [.. message.Questions.Select((question, index) => new TestQuestionCopy
            {
                CourseVersionId = message.CourseVersionId,
                QuestionId = question.QuestionId,
                Position = index + 1,
                ChapterId = question.ChapterId,
                Type = TypeOf(question.Type),
                Text = question.Text,
                Options = [.. question.Options.Select((option, position) => new TestOptionCopy
                {
                    CourseVersionId = message.CourseVersionId,
                    QuestionId = question.QuestionId,
                    OptionId = option.AnswerOptionId,
                    Position = position + 1,
                    Text = option.Text,
                    IsCorrect = option.IsCorrect,
                })],
            })],
        });
        await unitOfWork.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Editorial's publish invariants, refused here each by its field's name, so a poison message dead-letters with a
    /// reason that says which: a definition that breaks one could never be passed.
    /// </summary>
    private static void Refuse(TestDefinitionPublishedV1 message)
    {
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.PassingScorePercent, nameof(message.PassingScorePercent)).InRange(0, 100);
        Ensure.That(message.MaxAttempts, nameof(message.MaxAttempts)).AtLeast(1);
        Ensure.That(message.Questions, nameof(message.Questions)).IsNotNull();
        Ensure.That(message.Questions.Count, nameof(message.Questions)).AtLeast(1);

        HashSet<Guid> questionIds = [];
        foreach (var question in message.Questions)
        {
            Ensure.That(question, nameof(message.Questions)).IsNotNull();
            Refuse(question, questionIds);
        }
    }

    private static void Refuse(TestQuestionV1 question, HashSet<Guid> questionIds)
    {
        Ensure.That(question.QuestionId, nameof(question.QuestionId)).IsNotEmpty();
        Ensure.That(question.QuestionId.ToString(), nameof(question.QuestionId)).Satisfies(_ => questionIds.Add(question.QuestionId), "A question id may appear once in a test definition.");
        Ensure.That(question.ChapterId, nameof(question.ChapterId)).IsNotEmpty();
        Ensure.That(question.Text, nameof(question.Text)).IsNotNullOrWhiteSpace();
        Ensure.That(question.Options, nameof(question.Options)).IsNotNull();
        Ensure.That(question.Options.Count, nameof(question.Options)).AtLeast(1);

        HashSet<Guid> optionIds = [];
        foreach (var option in question.Options)
        {
            Ensure.That(option, nameof(question.Options)).IsNotNull();
            Ensure.That(option.AnswerOptionId, nameof(option.AnswerOptionId)).IsNotEmpty();
            Ensure.That(option.AnswerOptionId.ToString(), nameof(option.AnswerOptionId)).Satisfies(_ => optionIds.Add(option.AnswerOptionId), "An option id may appear once in its question.");
            Ensure.That(option.Text, nameof(option.Text)).IsNotNullOrWhiteSpace();
        }

        var correct = question.Options.Count(option => option.IsCorrect);
        if (question.Type == QuestionTypeV1.SingleChoice)
        {
            Ensure.That(correct, nameof(AnswerOptionV1.IsCorrect)).Satisfies(count => count == 1, "A single choice question has exactly one correct option.");
        }
        else
        {
            Ensure.That(correct, nameof(AnswerOptionV1.IsCorrect)).AtLeast(1);
        }
    }

    private static TestQuestionType TypeOf(QuestionTypeV1 type) => type switch
    {
        QuestionTypeV1.SingleChoice => TestQuestionType.SingleChoice,
        QuestionTypeV1.MultipleChoice => TestQuestionType.MultipleChoice,
        _ => throw new InvalidOperationException($"Question type {type} has no counterpart in Assessment."),
    };
}
