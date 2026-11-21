using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>A chapter id no chapter can have, such as the empty Guid: not found, with the code the domain gives a missing chapter.</summary>
public sealed record UnknownChapter(Guid Version, Guid Chapter)
    : ApiError("editorial.chapter-not-found", $"Course version {Version} has no chapter {Chapter}.", HttpStatusCode.NotFound);
