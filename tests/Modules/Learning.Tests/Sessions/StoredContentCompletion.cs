using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// A stored ContentCompleted with no marker (#277), as a test plants it: the event, its version in its stream and when it
/// was stored. The stream is the event's session unless <see cref="Stream"/> names another, as the event store would if
/// the two ever differed.
/// </summary>
internal sealed record StoredContentCompletion(ContentCompleted Completed, long StreamVersion, DateTimeOffset StoredAt, Guid? Stream = null);
