namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

/// <summary>A chapter of a course version. Its file is never answered: the player gets media from Learning.</summary>
public sealed record CatalogChapterDto(Guid ChapterId, int Position, string Title, CatalogChapterKind Kind, int? VideoDurationSeconds, string? Text);
