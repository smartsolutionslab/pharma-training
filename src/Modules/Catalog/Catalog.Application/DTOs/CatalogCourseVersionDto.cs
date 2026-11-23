namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

/// <summary>
/// A course version with its chapters in reading order and its status in the catalog: current, superseded by a newer
/// version of its course, or retired. <see cref="IsRetired"/> is <c>Status == Retired</c>.
/// </summary>
public sealed record CatalogCourseVersionDto(
    Guid CourseId,
    Guid CourseVersionId,
    int VersionNumber,
    string Title,
    int ValidityMonths,
    bool IsRetired,
    CatalogCourseVersionStatus Status,
    IReadOnlyList<CatalogChapterDto> Chapters);
