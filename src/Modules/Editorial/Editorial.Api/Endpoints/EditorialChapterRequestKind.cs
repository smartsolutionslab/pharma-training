using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The kind a request names for a chapter or an upload; travels by name. Only requests take it, so the kind the
/// module's answers carry, <c>EditorialChapterKind</c>, is never optional where a request leaves it out.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialChapterRequestKind>))]
public enum EditorialChapterRequestKind
{
    Text,
    Pdf,
    Video,
}
