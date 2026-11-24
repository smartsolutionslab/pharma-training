namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Whether an editor has changed a question since it came into being (#245, the owner's decision of 2026-11-24). An edit
/// is human work: the next AI draft never replaces an edited question. A copy keeps it, as it keeps the provenance.
/// </summary>
public enum EditHistory
{
    Untouched,
    Edited,
}
