using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names more people than one request may assign.</summary>
public sealed record TooManyLearners()
    : ApiError("compliance.too-many-learners", $"Assign at most {AssignCourse.MaximumLearners} persons at once.", HttpStatusCode.BadRequest);
