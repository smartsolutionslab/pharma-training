using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

[JsonConverter(typeof(JsonStringEnumConverter<AssessmentQuestionType>))]
public enum AssessmentQuestionType
{
    SingleChoice,
    MultipleChoice,
}
