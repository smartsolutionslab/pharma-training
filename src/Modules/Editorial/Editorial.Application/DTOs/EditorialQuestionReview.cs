using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>Whether an editor has signed a question off, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialQuestionReview>))]
public enum EditorialQuestionReview
{
    Unreviewed,
    Approved,
}
