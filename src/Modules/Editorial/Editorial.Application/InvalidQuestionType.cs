using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidQuestionType()
    : ApiError("editorial.invalid-question-type", "A question's type is SingleChoice or MultipleChoice.", HttpStatusCode.BadRequest);
