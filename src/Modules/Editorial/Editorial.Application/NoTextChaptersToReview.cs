using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The AI content review reads only text chapters, and the version has none.</summary>
public sealed record NoTextChaptersToReview()
    : ApiError("editorial.no-text-chapters-to-review", "The version has no text chapter to review.", HttpStatusCode.Conflict);
