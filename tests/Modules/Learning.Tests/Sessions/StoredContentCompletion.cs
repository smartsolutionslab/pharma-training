using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>A stored ContentCompleted with no marker (#277), as a test plants it: the event, its version in its stream and when it was stored.</summary>
internal sealed record StoredContentCompletion(ContentCompleted Completed, long StreamVersion, DateTimeOffset StoredAt);
