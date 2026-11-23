namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

public sealed record DefinitionOfAnotherVersion(ExaminationId Examination, CourseVersionId CourseVersion)
    : DomainError("assessment.definition-of-another-version", "Die Testdefinition passt nicht zur Schulungsversion.");
