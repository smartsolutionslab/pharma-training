using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The AI test draft asks only about text chapters, and the version has none.</summary>
public sealed record NoTextChapters()
    : ApiError("editorial.no-text-chapters", "The version has no text chapter to draft test questions from.", HttpStatusCode.Conflict);
