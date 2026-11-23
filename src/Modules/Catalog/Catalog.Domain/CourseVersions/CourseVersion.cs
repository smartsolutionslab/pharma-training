namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

/// <summary>
/// A course version Editorial published, as Catalog keeps it: stored once, never changed. A course's current version is
/// its highest version number while that version is not retired; every older version reads as superseded.
/// </summary>
public sealed class CourseVersion
{
    /// <summary>The course version's identifier, as Editorial gave it.</summary>
    public Guid Id { get; set; }

    public Guid CourseId { get; set; }

    public int VersionNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public int ValidityMonths { get; set; }

    public List<CourseChapter> Chapters { get; set; } = [];
}
