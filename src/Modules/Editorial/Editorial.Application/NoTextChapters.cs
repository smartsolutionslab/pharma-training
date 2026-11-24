using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The AI test draft asks only about chapters with text (a text chapter, a PDF with extracted text), and the version has none.</summary>
public sealed record NoTextChapters()
    : ApiError("editorial.no-text-chapters", "The version has no chapter with text to draft test questions from.", HttpStatusCode.Conflict);
