using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;

/// <summary>
/// An editor publishes a version in review once every question is approved; with four eyes, not the one who
/// submitted it or last changed its test blueprint. The course's published version is superseded in the same save.
/// </summary>
public sealed record PublishCourseVersion(CourseVersionId Version, PersonId Editor) : ICommand<Result<Unit, ApiError>>;
