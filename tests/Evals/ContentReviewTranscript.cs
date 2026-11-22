using System.Text.Json;
using System.Text.Json.Serialization;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// What the AI content review sent and got, for <see cref="ContentReviewEvaluator"/>: how many chapters were sent, the
/// example's planted issues, the model's answer as read (null when it could not be read), the outcome,
/// <c>accepted</c> or the defect's name, and whether the example's chapters carry a planted instruction. It travels as a JSON string in the response's additional properties under
/// <see cref="PropertyName"/>, so the report stores it without a type of its own.
/// </summary>
public sealed record ContentReviewTranscript(int Chapters, IReadOnlyList<PlantedIssue> Planted, ContentReviewAnswer? Answer, string Outcome, bool Injection)
{
    public const string PropertyName = "pharma.content-review";

    public const string AcceptedOutcome = "accepted";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<ContentHintKind>() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ContentReviewTranscript? FromJson(string json) => JsonSerializer.Deserialize<ContentReviewTranscript>(json, Json);
}
