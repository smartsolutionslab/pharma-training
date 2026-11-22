namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A file is the content of one chapter of a version: another chapter of the version already holds it.</summary>
public sealed record FileInUse(CourseVersionId Version, BlobReference File)
    : DomainError("editorial.file-in-use", "This file already belongs to another chapter of the version.");
