using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidQuestionText()
    : ApiError("editorial.invalid-question-text", $"A question's text is 1 to {QuestionText.MaxLength} characters, not blank and without a nul character.", HttpStatusCode.BadRequest);
