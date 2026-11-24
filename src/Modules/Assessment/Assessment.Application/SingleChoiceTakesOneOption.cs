using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

public sealed record SingleChoiceTakesOneOption(Guid QuestionId)
    : ApiError("assessment.single-choice-takes-one-option", "The question is single choice: choose exactly one option.", HttpStatusCode.UnprocessableEntity);
