using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The kind of chapter an upload is for, Pdf or Video; travels by name. A text chapter takes no file, so Text is no
/// value. There is no 0, so a body that leaves the kind out has none rather than Pdf. Only the uploads request uses it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialUploadKind>))]
public enum EditorialUploadKind
{
    Pdf = 1,
    Video = 2,
}
