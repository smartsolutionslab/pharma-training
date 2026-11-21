namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

/// <summary>A course as the catalog lists it: its current version, without chapters.</summary>
public sealed record CatalogCourseDto(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths);
