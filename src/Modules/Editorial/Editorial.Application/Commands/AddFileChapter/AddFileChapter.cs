using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddFileChapter;

/// <summary>
/// Adds a PDF or video chapter, whose file was uploaded through a URL issued for this version, at the end of a draft
/// and answers the chapter's id; a video has its duration, a PDF none.
/// </summary>
public sealed record AddFileChapter(CourseVersionId Version, ChapterTitle Title, ChapterKind Kind, BlobReference File, VideoDuration? Duration)
    : ICommand<Result<Guid, ApiError>>;
