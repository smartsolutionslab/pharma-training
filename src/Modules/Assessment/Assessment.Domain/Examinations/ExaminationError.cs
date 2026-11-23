namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>Every way the examination's steps (StartAttempt, Answer, Submit) refuse; a new one fails the build until it is mapped.</summary>
public union ExaminationError(ExaminationNotUnlocked, NoOpenAttempt, QuestionNotInAttempt, DefinitionOfAnotherVersion);
