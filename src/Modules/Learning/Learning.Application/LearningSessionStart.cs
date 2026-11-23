namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>The session a start answers, and whether this start created it (rather than resumed it).</summary>
public sealed record LearningSessionStart(Guid Id, bool Created);
