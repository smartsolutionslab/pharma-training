namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

public sealed record DefinitionOfAnotherVersion(ExaminationId Examination, CourseVersionId CourseVersion)
    : DomainError("assessment.definition-of-another-version", "The test definition does not belong to this course version.");
