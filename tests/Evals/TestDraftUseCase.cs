using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// The AI test draft as the evals run it: the example's course version, the module's request and prompt, the model's
/// answer read by the module's drafter, and the module's checks, the composition of <see cref="TestDrafter.DraftAsync"/>
/// split so the raw answer is kept for scoring. The response's text renders the answer for the judged metrics; its
/// <see cref="TestDraftTranscript"/> is what <see cref="TestDraftEvaluator"/> scores.
/// </summary>
public static class TestDraftUseCase
{
    public const string Name = "ai-test-draft";

    public static async Task<ChatResponse> AnswerAsync(IChatClient model, EvalExample example, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(example).IsNotNull();

        var version = TestDraftExamples.Version(example);
        var request = TestDraftRequest.For(version);
        if (request.IsFailure)
        {
            throw new InvalidOperationException($"The example '{example.Input}' cannot be drafted: {request.Error.Code}. Fix the example.");
        }

        var asked = await TestDrafter.AskAsync(model, request.Value, cancellationToken);
        TestDraftAnswer? answer = null;
        string outcome;
        if (asked.IsFailure)
        {
            outcome = Outcome(asked.Error);
        }
        else
        {
            answer = asked.Value;
            var drafted = answer.ToDraftedQuestions(request.Value);
            outcome = drafted.IsSuccess ? TestDraftTranscript.AcceptedOutcome : Outcome(drafted.Error);
        }

        var transcript = new TestDraftTranscript(request.Value.Questions.Value, [.. request.Value.Chapters.Select(chapter => chapter.Quota.Value)], answer, outcome);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, Render(answer, outcome)))
        {
            AdditionalProperties = new() { [TestDraftTranscript.PropertyName] = transcript.ToJson() },
        };
    }

    private static string Outcome(ApiError error) =>
        error is AiTestDraftMalformed malformed ? malformed.Defect.ToString() : error.Code;

    /// <summary>"1. (Kapitel 2, Single choice) …" with "[x]" or "[ ]" before each option, or why nothing could be read.</summary>
    private static string Render(TestDraftAnswer? answer, string outcome)
    {
        if (answer is null)
        {
            return $"No readable draft: {outcome}";
        }

        var text = new StringBuilder();
        for (var index = 0; index < answer.Questions.Count; index++)
        {
            var question = answer.Questions[index];
            var type = question.Type == TestDraftAnswerType.MultipleChoice ? "Multiple choice" : "Single choice";
            text.Append(CultureInfo.InvariantCulture, $"{index + 1}. (Kapitel {question.Chapter}, {type}) {question.Text}\n");
            foreach (var option in question.Options)
            {
                text.Append(CultureInfo.InvariantCulture, $"   [{(option.Correct ? "x" : " ")}] {option.Text}\n");
            }
        }

        return text.Length == 0 ? "No questions drafted." : text.ToString();
    }
}
