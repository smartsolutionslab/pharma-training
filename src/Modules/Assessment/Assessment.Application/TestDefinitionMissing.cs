using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>The test definition of the examination's course version has not arrived from Editorial yet (ADR 0012).</summary>
public sealed record TestDefinitionMissing(Guid CourseVersionId)
    : ApiError("assessment.test-definition-missing", "The test of this course version has not arrived yet; try again later.", HttpStatusCode.Conflict);
