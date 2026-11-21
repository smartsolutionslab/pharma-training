using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddTextChapter;

/// <summary>Adds a Markdown chapter at the end of a draft and answers the chapter's id.</summary>
public sealed record AddTextChapter(CourseVersionId Version, ChapterTitle Title, MarkdownText Text) : ICommand<Result<Guid, ApiError>>;
