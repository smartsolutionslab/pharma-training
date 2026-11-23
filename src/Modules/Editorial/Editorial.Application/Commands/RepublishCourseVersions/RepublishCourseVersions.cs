using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;

/// <summary>
/// An editor sends again what Editorial announced about its course versions, for a subscriber that bound its queue
/// after the events went out (#189, ADR 0019).
/// </summary>
public sealed record RepublishCourseVersions() : ICommand<Result<EditorialRepublishedEventsDto, ApiError>>;
