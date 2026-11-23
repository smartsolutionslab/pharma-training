namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>The body of <c>POST /editorial/course-versions/{id}/return</c>: why the editor returns the version to draft.</summary>
public sealed record ReturnEditorialCourseVersionRequest(string Reason);
