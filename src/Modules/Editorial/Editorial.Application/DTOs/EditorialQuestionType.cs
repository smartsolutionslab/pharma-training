using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// How many answers of a question are correct, as the module's endpoints name it; travels by name. There is no 0, so
/// a request that leaves the type out has none rather than single choice.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialQuestionType>))]
public enum EditorialQuestionType
{
    SingleChoice = 1,
    MultipleChoice = 2,
}
