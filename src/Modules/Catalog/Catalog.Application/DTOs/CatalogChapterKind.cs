using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

/// <summary>What a chapter holds, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CatalogChapterKind>))]
public enum CatalogChapterKind
{
    Text,
    Pdf,
    Video,
}
