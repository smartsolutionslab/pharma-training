namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

/// <summary>A course version with its chapters in reading order, retired or not.</summary>
public sealed record CatalogCourseVersionDto(
    Guid CourseId,
    Guid CourseVersionId,
    int VersionNumber,
    string Title,
    int ValidityMonths,
    bool IsRetired,
    IReadOnlyList<CatalogChapterDto> Chapters);
