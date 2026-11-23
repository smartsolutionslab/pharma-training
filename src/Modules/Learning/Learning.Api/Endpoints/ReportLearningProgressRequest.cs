namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api.Endpoints;

/// <summary>The body of <c>POST /learning/sessions/{id}/progress</c>: how far the learner got in one chapter, from 0 to 100.</summary>
public sealed record ReportLearningProgressRequest(Guid ChapterId, int Percent);
