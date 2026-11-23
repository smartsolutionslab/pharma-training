namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

public sealed record SessionFinishedAlready(LearningSessionId Session)
    : DomainError("learning.session-finished", "Diese Schulung ist bereits abgeschlossen.");
