using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment is due further ahead than any request may set: the caller's mistake, not a plan.</summary>
public sealed record DueDateTooFar()
    : ApiError("compliance.due-date-too-far", $"The due date is more than {AssignCourse.MaximumYearsAhead} years in the future.", HttpStatusCode.BadRequest);
