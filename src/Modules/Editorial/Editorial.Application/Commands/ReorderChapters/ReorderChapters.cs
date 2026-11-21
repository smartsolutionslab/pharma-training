using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReorderChapters;

/// <summary>Puts the chapters of a draft in this order; it names each of them exactly once.</summary>
public sealed record ReorderChapters(CourseVersionId Version, IReadOnlyList<ChapterId> Order) : ICommand<Result<Unit, ApiError>>;
