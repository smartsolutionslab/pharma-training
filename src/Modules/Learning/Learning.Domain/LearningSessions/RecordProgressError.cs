namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>Every way <see cref="LearningSession.RecordProgress"/> refuses; a new one fails the build until it is mapped.</summary>
public union RecordProgressError(SessionFinishedAlready, ChapterNotInCourse);
