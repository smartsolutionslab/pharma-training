using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>A drafted question's type as the model names it, by name in the answer's JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TestDraftAnswerType>))]
public enum TestDraftAnswerType
{
    SingleChoice,
    MultipleChoice,
}
