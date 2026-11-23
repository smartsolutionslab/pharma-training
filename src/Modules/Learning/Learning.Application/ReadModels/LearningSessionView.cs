using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// A learning session as its events leave it, kept by an inline projection of its stream in the same transaction as
/// the stream: what the start looks up by learner and version, and what a session's reads answer.
/// </summary>
public sealed class LearningSessionView
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid CourseId { get; set; }

    public Guid CourseVersionId { get; set; }

    public SessionStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public List<LearningSessionChapterView> Chapters { get; set; } = [];
}
