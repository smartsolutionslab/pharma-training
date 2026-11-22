using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidQuestionCount()
    : ApiError("editorial.invalid-question-count", $"A test has {TestBlueprint.MinimumQuestions} to {QuestionCount.Maximum} questions.", HttpStatusCode.BadRequest);
