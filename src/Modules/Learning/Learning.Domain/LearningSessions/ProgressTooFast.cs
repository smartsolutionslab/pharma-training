namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

public sealed record ProgressTooFast(LearningSessionId Session, ChapterId Chapter)
    : DomainError("learning.progress-too-fast", "The reported progress grows faster than the video can be played.");
