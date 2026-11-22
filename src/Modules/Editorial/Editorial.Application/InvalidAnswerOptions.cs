using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidAnswerOptions()
    : ApiError("editorial.invalid-answer-options", $"A question's answer options are a list of at most {DraftedQuestion.MaxOptions}, and none of them is null.", HttpStatusCode.BadRequest);
