namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>Everything the Examination aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
public static class ExaminationErrors
{
    public static DomainError NotUnlocked(ExaminationId examination, ExaminationStatus status) => new ExaminationNotUnlocked(examination, status);

    public static DomainError NoOpenAttempt(ExaminationId examination) => new NoOpenAttempt(examination);

    public static DomainError QuestionNotInAttempt(ExaminationId examination, QuestionVersionId question) => new QuestionNotInAttempt(examination, question);

    public static DomainError DefinitionOfAnotherVersion(ExaminationId examination, CourseVersionId courseVersion) => new DefinitionOfAnotherVersion(examination, courseVersion);
}
