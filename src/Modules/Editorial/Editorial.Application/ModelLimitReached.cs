using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// An editor reached a limit on the endpoints that call the model (docs/adr/0020): one code for both, since the
/// request says which endpoint refused.
/// </summary>
public sealed record ModelLimitReached()
    : ApiError("editorial.model-limit-reached", "You have reached your limit of AI requests; try again later.", HttpStatusCode.TooManyRequests);
