using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>A hint's kind as the model names it, by name in the answer's JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentReviewAnswerKind>))]
public enum ContentReviewAnswerKind
{
    Gap,
    Contradiction,
    Readability,
}
