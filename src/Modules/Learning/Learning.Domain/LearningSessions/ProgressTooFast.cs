namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

public sealed record ProgressTooFast(LearningSessionId Session, ChapterId Chapter)
    : DomainError("learning.progress-too-fast", "Der gemeldete Fortschritt wächst schneller, als das Video abgespielt werden kann.");
