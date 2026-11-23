using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The kind a request names for a chapter; travels by name. Text is its default, so a body that names no
/// kind binds to Text. It is not nullable: the OpenAPI document would write <c>null</c> among the values of an enum
/// first met as nullable, and every place naming the component would take it. Only requests use it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialChapterRequestKind>))]
public enum EditorialChapterRequestKind
{
    Text = 0,
    Pdf,
    Video,
}
