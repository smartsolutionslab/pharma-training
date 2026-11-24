using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

public sealed record OptionNotInQuestion(Guid QuestionId, Guid OptionId)
    : ApiError("assessment.option-not-in-question", $"The option {OptionId} is not one of the question {QuestionId}'s.", HttpStatusCode.UnprocessableEntity);
