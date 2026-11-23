namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A question paper whose options say which of them is correct, two lists deep.</summary>
public sealed record ProbeQuestionPaperResponse(Guid AttemptId, IReadOnlyList<ProbePaperQuestion> Questions);
