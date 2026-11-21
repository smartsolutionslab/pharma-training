namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>The body of <c>PUT /editorial/course-versions/{id}/chapters/order</c>: every chapter's id, in the new order.</summary>
public sealed record ReorderEditorialChaptersRequest(IReadOnlyList<Guid>? Chapters);
