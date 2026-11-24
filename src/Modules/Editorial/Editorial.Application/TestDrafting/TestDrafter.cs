using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// Drafts a course version's test with the model (ADR 0008). The model is asked for JSON of
/// <see cref="TestDraftAnswer"/>'s schema; its answer is untrusted input, read and checked here, and never applied
/// unless every check passes. Exceptions from the model's client are not caught: a broken provider is no expected failure.
/// </summary>
public static class TestDrafter
{
    public const string SchemaName = "test_draft";

    /// <summary>
    /// The answer's output tokens allowed per question asked for, so a runaway answer is cut off rather than billed in
    /// full. A sound question with four options is some 150 to 250 tokens of JSON, and gpt-5-mini counts its reasoning
    /// against the same limit, so 1,280 leaves ample room; at <see cref="QuestionCount.Maximum"/> questions it is 64,000,
    /// half the model's own output ceiling.
    /// </summary>
    public const int MaxOutputTokensPerQuestion = 1_280;

    private static readonly JsonSerializerOptions AnswerJson = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter<TestDraftAnswerType>(namingPolicy: null, allowIntegerValues: false) },
    };

    private static readonly ChatResponseFormat ResponseFormat =
        ChatResponseFormat.ForJsonSchema<TestDraftAnswer>(AnswerJson, SchemaName, "The drafted questions of a course's test.");

    /// <summary>Asks the model for the draft and reads its answer into <see cref="TestDraftAnswer"/>, or reports it unreadable.</summary>
    public static async Task<Result<TestDraftAnswer, ApiError>> AskAsync(IChatClient model, TestDraftRequest request, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(request).IsNotNull();

        var options = new ChatOptions
        {
            ResponseFormat = ResponseFormat,
            MaxOutputTokens = request.Questions.Value * MaxOutputTokensPerQuestion,
        };
        var response = await model.GetResponseAsync(TestDraftPrompt.Messages(request), options, cancellationToken);
        return Read(response.Text);
    }

    /// <summary>
    /// The whole draft: the request from the version (refused before the model is called when it cannot be drafted),
    /// the model's answer, the drafted questions it maps onto and the chapters left out. A failure returns no questions at all.
    /// </summary>
    public static async Task<Result<TestDraft, ApiError>> DraftAsync(IChatClient model, CourseVersion version, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(version).IsNotNull();

        var request = TestDraftRequest.For(version);
        if (request.IsFailure)
        {
            return Failure(request.Error);
        }

        var answer = await AskAsync(model, request.Value, cancellationToken);
        if (answer.IsFailure)
        {
            return Failure(answer.Error);
        }

        var drafted = answer.Value.ToDraftedQuestions(request.Value);
        if (drafted.IsFailure)
        {
            return Failure(drafted.Error);
        }

        return Success(new TestDraft(drafted.Value, request.Value.LeftOutChapters, request.Value.TruncatedChapters));
    }

    private static Result<TestDraftAnswer, ApiError> Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unreadable();
        }

        TestDraftAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<TestDraftAnswer>(text, AnswerJson);
        }
        catch (JsonException)
        {
            return Unreadable();
        }

        // The serializer refuses a null property, but not a null element of a list: those are checked here.
        if (answer is null || answer.Questions.Any(question => question is null || question.Options.Any(option => option is null)))
        {
            return Unreadable();
        }

        return Success(answer);
    }

    private static Result<TestDraftAnswer, ApiError> Unreadable() =>
        Failure(CourseVersionFailures.AiTestDraftMalformed(TestDraftDefect.Unreadable));
}
