using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveChapter;

/// <summary>Removes a chapter from a draft, with the questions that ask about it.</summary>
public sealed record RemoveChapter(CourseVersionId Version, ChapterId Chapter) : ICommand<Result<Unit, ApiError>>;
