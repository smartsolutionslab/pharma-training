namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>What a failed attempt may report: its score, its outcome and the chapters to repeat, never the right answers.</summary>
public sealed record ProbeAttemptResultResponse(Guid AttemptId, int ScorePercent, string Outcome, IReadOnlyList<ProbeFailedChapter> FailedChapters);
