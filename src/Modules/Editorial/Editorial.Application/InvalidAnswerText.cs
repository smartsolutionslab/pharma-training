using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidAnswerText()
    : ApiError("editorial.invalid-answer-text", $"An answer option's text is 1 to {AnswerText.MaxLength} characters, not blank and without a nul character.", HttpStatusCode.BadRequest);
