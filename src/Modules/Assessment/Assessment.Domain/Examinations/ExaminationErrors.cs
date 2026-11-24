namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>Everything the Examination aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
public static class ExaminationErrors
{
    public static ExaminationError NotUnlocked(ExaminationId examination, ExaminationStatus status) => new ExaminationNotUnlocked(examination, status);

    public static ExaminationError NoOpenAttempt(ExaminationId examination) => new NoOpenAttempt(examination);

    public static ExaminationError QuestionNotInAttempt(ExaminationId examination, QuestionVersionId question) => new QuestionNotInAttempt(examination, question);

    public static ExaminationError DefinitionOfAnotherVersion(ExaminationId examination, CourseVersionId courseVersion) => new DefinitionOfAnotherVersion(examination, courseVersion);
}
