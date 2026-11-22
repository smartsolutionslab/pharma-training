using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;

/// <summary>A version in review goes back to its editors as a draft, and takes changes again.</summary>
public sealed record ReturnToDraft(CourseVersionId Version) : ICommand<Result<Unit, ApiError>>;
