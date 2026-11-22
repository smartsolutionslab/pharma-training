using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;

/// <summary>
/// Makes a chapter of a draft a PDF or video, in its place: a file uploaded through a URL issued for this version, or
/// the file the chapter already has; a video has its duration, a PDF none.
/// </summary>
public sealed record ReplaceFileChapter(CourseVersionId Version, ChapterId Chapter, ChapterTitle Title, ChapterKind Kind, BlobReference File, VideoDuration? Duration)
    : ICommand<Result<Unit, ApiError>>;
