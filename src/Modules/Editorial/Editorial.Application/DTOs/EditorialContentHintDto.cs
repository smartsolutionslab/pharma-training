namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>One hint on one chapter. Its text is model output: show it as plain text, never as Markdown or HTML.</summary>
public sealed record EditorialContentHintDto(Guid ChapterId, EditorialContentHintKind Kind, string Text);
