using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The AI content review reads text chapters and PDF chapters with extracted text, and the version has none.</summary>
public sealed record NoTextChaptersToReview()
    : ApiError("editorial.no-text-chapters-to-review", "The version has no chapter with text to review.", HttpStatusCode.Conflict);
