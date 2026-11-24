using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>A stored ContentCompleted with no marker (#277): the event, its version in the session's stream and when Marten stored it.</summary>
public sealed record StoredContentCompletion(ContentCompleted Completed, long StreamVersion, DateTimeOffset StoredAt);
