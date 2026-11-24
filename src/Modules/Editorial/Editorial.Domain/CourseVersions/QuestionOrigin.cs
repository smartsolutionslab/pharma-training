namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// How a question came into its version (#245): drafted by the AI, copied from the published version the draft was
/// started from, or written by hand. An AI draft replaces only unreviewed AI-drafted questions. Unlike
/// <see cref="Provenance"/>, which an AI-drafted question keeps for good, a copy is <see cref="Copied"/> in the next version.
/// </summary>
public enum QuestionOrigin
{
    AiDraft,
    Copied,
    HandWritten,
}
