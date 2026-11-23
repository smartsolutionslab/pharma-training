namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The body that adds a chapter to a version or replaces one: its title and its kind, Text when it names none. A text
/// chapter has its Markdown <c>Text</c>; a PDF its <c>File</c>, the blob name an upload answered; a video its
/// <c>File</c> and its <c>DurationSeconds</c>. Everything but the title has a default, so the OpenAPI document requires
/// only the title, as the endpoint does: what a kind does not take may be left out, and so may the kind.
/// </summary>
public sealed record EditorialChapterRequest(
    string Title,
    EditorialChapterRequestKind Kind = EditorialChapterRequestKind.Text,
    string? Text = null,
    string? File = null,
    int? DurationSeconds = null);
