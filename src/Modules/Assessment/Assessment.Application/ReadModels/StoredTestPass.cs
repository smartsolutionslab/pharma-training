using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

/// <summary>A stored ExaminationPassed with no marker (#277): the event, its version in the examination's stream and when Marten stored it.</summary>
public sealed record StoredTestPass(ExaminationPassed Passed, long StreamVersion, DateTimeOffset StoredAt);
