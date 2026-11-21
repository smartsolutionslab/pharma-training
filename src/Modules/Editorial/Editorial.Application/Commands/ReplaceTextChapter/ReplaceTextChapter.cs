using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceTextChapter;

/// <summary>Gives a chapter of a draft a new title and Markdown text, in its place.</summary>
public sealed record ReplaceTextChapter(CourseVersionId Version, ChapterId Chapter, ChapterTitle Title, MarkdownText Text) : ICommand<Result<Unit, ApiError>>;
