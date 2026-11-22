using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// Reviews a course version's text chapters with the model (ADR 0009). The model is asked for JSON of
/// <see cref="ContentReviewAnswer"/>'s schema; its answer is untrusted input, read and checked here, and never returned
/// unless every check passes. Nothing is saved. Exceptions from the model's client are not caught: a broken provider is
/// no expected failure.
/// </summary>
public static class ContentReviewer
{
    public const string SchemaName = "content_review";

    private static readonly JsonSerializerOptions AnswerJson = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter<ContentReviewAnswerKind>(namingPolicy: null, allowIntegerValues: false) },
    };

    private static readonly ChatResponseFormat ResponseFormat =
        ChatResponseFormat.ForJsonSchema<ContentReviewAnswer>(AnswerJson, SchemaName, "Hints on a course's chapters.");

    /// <summary>Asks the model for the review and reads its answer into <see cref="ContentReviewAnswer"/>, or reports it unreadable.</summary>
    public static async Task<Result<ContentReviewAnswer, ApiError>> AskAsync(IChatClient model, ContentReviewRequest request, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(request).IsNotNull();

        var options = new ChatOptions { ResponseFormat = ResponseFormat };
        var response = await model.GetResponseAsync(ContentReviewPrompt.Messages(request), options, cancellationToken);
        return Read(response.Text);
    }

    /// <summary>
    /// The whole review: the request from the version (refused before the model is called when it cannot be reviewed),
    /// the model's answer, and the checked hints it maps onto. A failure returns no hints at all.
    /// </summary>
    public static async Task<Result<ContentReview, ApiError>> ReviewAsync(IChatClient model, CourseVersion version, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(version).IsNotNull();

        var request = ContentReviewRequest.For(version);
        if (request.IsFailure)
        {
            return Failure(request.Error);
        }

        var answer = await AskAsync(model, request.Value, cancellationToken);
        if (answer.IsFailure)
        {
            return Failure(answer.Error);
        }

        return answer.Value.ToReview(request.Value);
    }

    private static Result<ContentReviewAnswer, ApiError> Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unreadable();
        }

        ContentReviewAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<ContentReviewAnswer>(text, AnswerJson);
        }
        catch (JsonException)
        {
            return Unreadable();
        }

        // The serializer refuses a null property, but not a null element of a list: those are checked here.
        if (answer is null || answer.Hints.Any(hint => hint is null))
        {
            return Unreadable();
        }

        return Success(answer);
    }

    private static Result<ContentReviewAnswer, ApiError> Unreadable() =>
        Failure(ContentReviewFailures.Malformed(ContentReviewDefect.Unreadable));
}
