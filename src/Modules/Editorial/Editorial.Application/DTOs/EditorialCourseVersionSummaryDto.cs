namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A course version in a list, without its chapters.</summary>
public sealed record EditorialCourseVersionSummaryDto(Guid Id, Guid CourseId, int Number, string Title, EditorialCourseVersionStatus Status);
