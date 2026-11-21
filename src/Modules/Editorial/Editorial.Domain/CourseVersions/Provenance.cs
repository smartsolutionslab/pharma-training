namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>Where a question came from. An AI-drafted question stays AiDrafted after a human edits it.</summary>
public enum Provenance
{
    Human,
    AiDrafted,
}
