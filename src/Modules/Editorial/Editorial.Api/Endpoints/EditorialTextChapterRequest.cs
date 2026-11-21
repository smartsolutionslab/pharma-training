namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>The body that adds a Markdown chapter to a version or replaces one: its title and its Markdown text.</summary>
public sealed record EditorialTextChapterRequest(string Title, string Text);
