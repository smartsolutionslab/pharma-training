namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The stored event cannot be read or mapped: it is noted for the pass's one Warning (<see cref="UnreadableEvents"/>)
/// and skipped, and it keeps no marker, so every pass tries it again until the lookback.
/// </summary>
/// <param name="Failure">Why it could not be read or mapped; logged by its type alone.</param>
public sealed record UnreadableSweptEvent(Exception Failure);
