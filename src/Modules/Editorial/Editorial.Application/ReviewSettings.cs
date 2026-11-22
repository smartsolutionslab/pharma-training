using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// How a version in review is published: by any editor, or with four eyes not by the one who submitted it. The host
/// reads it once at startup (ADR 0007); every publish takes the same settings.
/// </summary>
public sealed record ReviewSettings(ReviewPolicy Policy)
{
    /// <summary>Four eyes off, as long as the editorial team is one person.</summary>
    public static ReviewSettings Default => new(ReviewPolicy.SingleEditor);
}
