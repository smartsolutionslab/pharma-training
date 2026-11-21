using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>A question id no question can have, such as the empty Guid: not found, with the code the domain gives a missing question.</summary>
public sealed record UnknownQuestion(Guid Version, Guid Question)
    : ApiError("editorial.question-not-found", $"Course version {Version} has no question {Question}.", HttpStatusCode.NotFound);
