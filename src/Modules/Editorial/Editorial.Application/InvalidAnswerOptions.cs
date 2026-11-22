using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidAnswerOptions()
    : ApiError("editorial.invalid-answer-options", "A question's answer options are a list, and none of them is null.", HttpStatusCode.BadRequest);
