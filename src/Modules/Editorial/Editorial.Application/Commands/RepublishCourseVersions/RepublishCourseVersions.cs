using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;

/// <summary>
/// An editor sends again what Editorial announced about its course versions, for a subscriber that bound its queue
/// after the events went out (#189, ADR 0019). The editor is named in the run's log line.
/// </summary>
public sealed record RepublishCourseVersions(PersonId Editor) : ICommand<Result<EditorialRepublishedEventsDto, ApiError>>;
