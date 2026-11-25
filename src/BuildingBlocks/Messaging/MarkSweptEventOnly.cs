namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The stored event has no announcement of its own (Assessment's passed AttemptSubmitted, announced by its
/// ExaminationPassed): it is settled with its marker alone, and nothing is announced or logged for it.
/// </summary>
public sealed record MarkSweptEventOnly;
