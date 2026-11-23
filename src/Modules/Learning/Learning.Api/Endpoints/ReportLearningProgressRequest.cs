namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api.Endpoints;

/// <summary>The body of <c>POST /learning/sessions/{id}/progress</c>: how far the learner got in one chapter, from 0 to 100. A body
/// without <c>percent</c> is refused like one out of range, rather than read as 0.</summary>
public sealed record ReportLearningProgressRequest(Guid ChapterId, int? Percent);
