using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>Where a question came from, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialQuestionProvenance>))]
public enum EditorialQuestionProvenance
{
    Human,
    AiDrafted,
}
