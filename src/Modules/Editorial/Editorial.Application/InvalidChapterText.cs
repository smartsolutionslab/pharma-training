using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidChapterText()
    : ApiError("editorial.invalid-chapter-text", $"A text chapter's Markdown is 1 to {MarkdownText.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
