using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SetTestBlueprint;

/// <summary>Gives a draft the test blueprint it is to be tested by: how many questions, the passing score and how long a certificate is valid.</summary>
public sealed record SetTestBlueprint(CourseVersionId Version, TestBlueprint Blueprint) : ICommand<Result<Unit, ApiError>>;
