using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// Reads a hint's kind from the model's answer strictly: a JSON string that is exactly one of
/// <see cref="ContentReviewAnswerKind"/>'s names, compared ordinally. <see cref="JsonStringEnumConverter{TEnum}"/> would
/// read <c>"Gap, Readability"</c> as the flags <c>Gap | Readability</c>, a defined value or none, so it is used for the
/// answer's schema only, never to read the answer. Anything else throws <see cref="JsonException"/>, which the reviewer
/// reports as unreadable.
/// </summary>
public sealed class ContentReviewAnswerKindConverter : JsonConverter<ContentReviewAnswerKind>
{
    private static readonly FrozenDictionary<string, ContentReviewAnswerKind> Kinds =
        Enum.GetValues<ContentReviewAnswerKind>().ToFrozenDictionary(kind => kind.ToString(), StringComparer.Ordinal);

    public override ContentReviewAnswerKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || reader.GetString() is not { } name
            || !Kinds.TryGetValue(name, out var kind)
            || !Enum.IsDefined(kind))
        {
            throw new JsonException("A hint's kind is not exactly one of the defined names.");
        }

        return kind;
    }

    public override void Write(Utf8JsonWriter writer, ContentReviewAnswerKind value, JsonSerializerOptions options)
    {
        Ensure.That(writer).IsNotNull();
        if (!Enum.IsDefined(value))
        {
            throw new JsonException("A hint's kind is not one of the defined names.");
        }

        writer.WriteStringValue(value.ToString());
    }
}
