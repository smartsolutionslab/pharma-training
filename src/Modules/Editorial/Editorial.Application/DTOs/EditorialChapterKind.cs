using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>What a chapter holds, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialChapterKind>))]
public enum EditorialChapterKind
{
    Text,
    Pdf,
    Video,
}
