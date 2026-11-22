namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A course version with its chapters and its questions in order and its test blueprint, as the module's endpoints answer it.</summary>
public sealed record EditorialCourseVersionDto(
    Guid Id,
    Guid CourseId,
    int Number,
    string Title,
    EditorialCourseVersionStatus Status,
    IReadOnlyList<EditorialChapterDto> Chapters,
    IReadOnlyList<EditorialQuestionDto> Questions,
    EditorialTestBlueprintDto Blueprint);
