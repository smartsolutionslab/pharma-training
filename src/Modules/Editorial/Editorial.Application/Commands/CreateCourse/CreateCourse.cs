using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.CreateCourse;

/// <summary>Starts a course with its first version, a draft by this editor, and answers the version's id.</summary>
public sealed record CreateCourse(CourseTitle Title, PersonId Editor) : ICommand<Result<Guid, ApiError>>;
