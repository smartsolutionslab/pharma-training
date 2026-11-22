using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidPassingScore()
    : ApiError("editorial.invalid-passing-score", $"A passing score is {ScorePercent.Minimum} to {ScorePercent.Maximum} percent.", HttpStatusCode.BadRequest);
