namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The body that adds a chapter to a version or replaces one: its title and its kind, Text when it names none. A text
/// chapter has its Markdown <c>Text</c>; a PDF its <c>File</c>, the blob name an upload answered; a video its
/// <c>File</c> and its <c>DurationSeconds</c>.
/// </summary>
public sealed record EditorialChapterRequest(string Title, EditorialChapterRequestKind? Kind, string? Text, string? File, int? DurationSeconds);
