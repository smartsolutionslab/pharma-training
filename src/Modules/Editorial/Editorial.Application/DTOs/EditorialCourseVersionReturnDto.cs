namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>One return of a course version: who returned it (the person's id), when, and the reason as the editor wrote it.</summary>
public sealed record EditorialCourseVersionReturnDto(Guid Id, Guid ReturnedBy, DateTimeOffset ReturnedAt, string Reason);
