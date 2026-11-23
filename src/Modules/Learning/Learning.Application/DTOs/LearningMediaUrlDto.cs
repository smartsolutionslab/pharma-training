namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

/// <summary>A read URL on a chapter's PDF or video, and when it stops working, so the player asks for a new one in time.</summary>
public sealed record LearningMediaUrlDto(Uri Url, DateTimeOffset ExpiresAt, string ContentType, LearningChapterKind Kind);
