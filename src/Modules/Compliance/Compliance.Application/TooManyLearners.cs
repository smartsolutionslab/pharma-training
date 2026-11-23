using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names more people than one request may assign.</summary>
public sealed record TooManyLearners(int Maximum)
    : ApiError("compliance.too-many-learners", $"Höchstens {Maximum} Personen auf einmal zuweisen.", HttpStatusCode.BadRequest);
