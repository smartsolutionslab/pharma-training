namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api.Endpoints;

/// <summary>The body of <c>POST /learning/sessions</c>: the course version to start or resume. The learner is the caller.</summary>
public sealed record StartLearningSessionRequest(Guid CourseVersionId);
