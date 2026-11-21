using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>How many answer options of a test question are correct; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuestionTypeV1>))]
public enum QuestionTypeV1
{
    SingleChoice,
    MultipleChoice,
}
