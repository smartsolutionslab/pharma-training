using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>A version Learning holds with a given chapter: the version, its number, and what the chapter holds there (#41).</summary>
public sealed record ChapterPlacement(Guid CourseVersionId, int VersionNumber, ChapterKind Kind);
