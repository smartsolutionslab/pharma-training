namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A chapter at its place in the version, counted from 1; <c>Text</c> is the Markdown of a text chapter and null otherwise.</summary>
public sealed record EditorialChapterDto(Guid Id, int Position, string Title, EditorialChapterKind Kind, string? Text);
