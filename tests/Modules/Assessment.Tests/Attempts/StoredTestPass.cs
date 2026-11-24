using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// A stored ExaminationPassed with no marker (#277), as a test plants it: the event, its version in its stream and when it
/// was stored. The stream is the event's examination unless <see cref="Stream"/> names another, as the event store would
/// if the two ever differed.
/// </summary>
internal sealed record StoredTestPass(ExaminationPassed Passed, long StreamVersion, DateTimeOffset StoredAt, Guid? Stream = null);
