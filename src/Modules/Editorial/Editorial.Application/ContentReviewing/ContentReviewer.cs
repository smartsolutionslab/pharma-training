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

    /// <summary>
    /// The model's per-hint allowance in output tokens beyond its text: at least one token per character counted
    /// generously, plus the hint's JSON.
    /// </summary>
    private const int OutputTokensPerHint = ContentHint.MaxTextLength + 100;

    /// <summary>
    /// What a reasoning deployment (gpt-5 and its kind) may spend thinking: its reasoning tokens count against the same
    /// <see cref="ChatOptions.MaxOutputTokens"/> as the answer, so a cap of the answer alone would cut it off.
    /// </summary>
    private const int ReasoningTokens = 16_384;

    /// <summary>
    /// The most output tokens a gpt-5 deployment accepts for one call; a larger <see cref="ChatOptions.MaxOutputTokens"/>
    /// is refused with a 400, so the cap stops here, for versions of 21 text chapters and more.
    /// </summary>
    private const int OutputTokenCeiling = 128_000;

    /// <summary>Strict numbers; kinds by their names alone, read by <see cref="ContentReviewAnswerKindConverter"/>.</summary>
    private static readonly JsonSerializerOptions AnswerJson = Options(new ContentReviewAnswerKindConverter());

    /// <summary>The same shape for the schema, whose exporter lists the kinds' names only for the enum converter.</summary>
    private static readonly JsonSerializerOptions SchemaJson =
        Options(new JsonStringEnumConverter<ContentReviewAnswerKind>(namingPolicy: null, allowIntegerValues: false));

    private static readonly ChatResponseFormat ResponseFormat =
        ChatResponseFormat.ForJsonSchema<ContentReviewAnswer>(SchemaJson, SchemaName, "Hints on a course's chapters.");

    /// <summary>Asks the model for the review and reads its answer into <see cref="ContentReviewAnswer"/>, or reports it unreadable.</summary>
    public static async Task<Result<ContentReviewAnswer, ApiError>> AskAsync(IChatClient model, ContentReviewRequest request, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(request).IsNotNull();

        // A full answer is at most MaxHintsPerChapter hints on every chapter sent; the model's reasoning tokens count
        // against the same budget, so the cap leaves room for them too.
        var options = new ChatOptions
        {
            ResponseFormat = ResponseFormat,
            MaxOutputTokens = Math.Min(
                (ContentReviewAnswer.MaxHintsPerChapter * request.Chapters.Count * OutputTokensPerHint) + ReasoningTokens,
                OutputTokenCeiling),
        };
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
        catch (ArgumentException)
        {
            // A raw lone surrogate cannot be transcoded to UTF-8: another IChatClient than OpenAI's could return one.
            return Unreadable();
        }

        // The serializer refuses a null property, but not a null element of a list: those are checked here.
        if (answer is null || answer.Hints.Any(hint => hint is null))
        {
            return Unreadable();
        }

        return Success(answer);
    }

    private static JsonSerializerOptions Options(JsonConverter kinds) => new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { kinds },
    };

    private static Result<ContentReviewAnswer, ApiError> Unreadable() =>
        Failure(ContentReviewFailures.Malformed(ContentReviewDefect.Unreadable));
}
