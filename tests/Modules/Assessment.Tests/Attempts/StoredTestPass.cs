using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>A stored ExaminationPassed with no marker (#277), as a test plants it: the event, its version in its stream and when it was stored.</summary>
internal sealed record StoredTestPass(ExaminationPassed Passed, long StreamVersion, DateTimeOffset StoredAt);
