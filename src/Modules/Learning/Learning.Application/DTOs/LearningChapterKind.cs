using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

/// <summary>What a chapter of a learning session holds, as the endpoints answer it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningChapterKind>))]
public enum LearningChapterKind
{
    Text,
    Pdf,
    Video,
}
