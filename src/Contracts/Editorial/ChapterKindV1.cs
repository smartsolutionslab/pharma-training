using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>What a chapter of a published course version holds; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChapterKindV1>))]
public enum ChapterKindV1
{
    Text,
    Pdf,
    Video,
}
