namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>Why approved questions of a version went back to review (#246).</summary>
public enum ApprovalResetReason
{
    TestSettingsChanged,
    QuestionEdited,
    ChapterReplaced,
}
