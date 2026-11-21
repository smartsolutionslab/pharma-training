namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A course version with its chapters in order, as the module's endpoints answer it.</summary>
public sealed record EditorialCourseVersionDto(Guid Id, Guid CourseId, int Number, string Title, EditorialCourseVersionStatus Status, IReadOnlyList<EditorialChapterDto> Chapters);
