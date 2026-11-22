using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SubmitForReview;

/// <summary>An editor submits a complete draft for review; who submitted it is recorded.</summary>
public sealed record SubmitForReview(CourseVersionId Version, PersonId Editor) : ICommand<Result<Unit, ApiError>>;
