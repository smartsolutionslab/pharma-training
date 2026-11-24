namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Whether an editor has ever approved a question (#245, the owner's decision of 2026-11-24), unlike
/// <see cref="ReviewState"/>, which an edit or a settings change (#199) sends back to unreviewed. An approval is human
/// work for good: the next AI draft never replaces a question approved once. A copy keeps it, as it keeps the approval.
/// </summary>
public enum ApprovalHistory
{
    NeverApproved,
    ApprovedOnce,
}
