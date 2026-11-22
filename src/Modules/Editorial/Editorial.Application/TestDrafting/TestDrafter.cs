using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// Drafts a course version's test with the model (ADR 0008). The model is asked for JSON of
/// <see cref="TestDraftAnswer"/>'s schema; its answer is untrusted input, read and checked here, and never applied
/// unless every check passes. Exceptions from the model's client are not caught: a broken provider is no expected failure.
/// </summary>
public static class TestDrafter
{
    public const string SchemaName = "test_draft";

    private static readonly JsonSerializerOptions AnswerJson = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter<TestDraftAnswerType>(namingPolicy: null, allowIntegerValues: false) },
    };

    private static readonly ChatResponseFormat ResponseFormat =
        ChatResponseFormat.ForJsonSchema<TestDraftAnswer>(AnswerJson, SchemaName, "The drafted questions of a course's test.");

    /// <summary>Asks the model for the draft and reads its answer into <see cref="TestDraftAnswer"/>, or reports it unreadable.</summary>
    public static async Task<Result<TestDraftAnswer, ApiError>> AskAsync(IChatClient model, TestDraftRequest request, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(request).IsNotNull();

        var options = new ChatOptions { ResponseFormat = ResponseFormat };
        var response = await model.GetResponseAsync(TestDraftPrompt.Messages(request), options, cancellationToken);
        return Read(response.Text);
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
