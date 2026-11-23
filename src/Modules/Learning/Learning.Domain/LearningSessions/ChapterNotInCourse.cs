namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

public sealed record ChapterNotInCourse(LearningSessionId Session, ChapterId Chapter)
    : DomainError("learning.chapter-not-in-course", "Dieses Kapitel gehört nicht zu dieser Schulungsversion.");
