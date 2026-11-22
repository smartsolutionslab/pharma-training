using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>What a content review hint points out, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialContentHintKind>))]
public enum EditorialContentHintKind
{
    Gap,
    Contradiction,
    Readability,
}
