using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RetireCourseVersion;

/// <summary>An editor retires a published or superseded version; Catalog and Compliance hear of it.</summary>
public sealed record RetireCourseVersion(CourseVersionId Version, PersonId Editor) : ICommand<Result<Unit, ApiError>>;
