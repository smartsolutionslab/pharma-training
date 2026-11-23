namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// One return of a course version: who returned it, when, and the reason as the editor wrote it. <see cref="ReturnedBy"/>,
/// the person's id, is authoritative; <see cref="ReturnedByName"/> is for display only, the name the editor's token
/// asserted at return time, and null for returns stored before #220.
/// </summary>
public sealed record EditorialCourseVersionReturnDto(Guid Id, Guid ReturnedBy, string? ReturnedByName, DateTimeOffset ReturnedAt, string Reason);
