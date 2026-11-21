using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidChapterTitle()
    : ApiError("editorial.invalid-chapter-title", $"A chapter's title is 1 to {ChapterTitle.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
