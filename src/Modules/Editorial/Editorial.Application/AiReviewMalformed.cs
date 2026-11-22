using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The model's answer to an AI content review broke the review's rules, so no hints are returned: the upstream model
/// failed, not the caller's request. The message is the same whatever the <see cref="Defect"/>, which only tests and
/// the evals read.
/// </summary>
public sealed record AiReviewMalformed(ContentReviewDefect Defect)
    : ApiError("editorial.ai-review-malformed", "The AI review could not be used because the model's answer broke the review's rules. Nothing was changed; try again.", HttpStatusCode.BadGateway);
