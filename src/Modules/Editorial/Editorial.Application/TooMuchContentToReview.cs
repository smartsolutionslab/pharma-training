using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The version's chapter titles and text chapters are more than one AI content review sends.</summary>
public sealed record TooMuchContentToReview()
    : ApiError("editorial.too-much-content-to-review", "The version's text chapters are too long to review in one request.", HttpStatusCode.UnprocessableEntity);
