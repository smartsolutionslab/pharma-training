using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// A chapter id no chapter can have, such as the empty Guid: not found, with the code and message of the domain's
/// <see cref="ChapterNotFound"/>, taken from it so the two cannot differ.
/// </summary>
public sealed record UnknownChapter(Guid Version, Guid Chapter)
    : ApiError(Refusal.Code, Refusal.Message, HttpStatusCode.NotFound)
{
    /// <summary>The domain's refusal, read for its code and message only.</summary>
    private static readonly ChapterNotFound Refusal = new(CourseVersionId.New(), ChapterId.New());
}
