namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// PostgreSQL ended the save as a deadlock (40P01) or a serialization failure (40001) with another save at the same
/// moment, or a statement of it waited for another save's lock past the transaction's <c>lock_timeout</c> (55P03). What the caller loaded may still be current, and its transaction rolled back, so repeating the same request
/// may simply succeed.
/// </summary>
/// <param name="SqlState">The SqlState PostgreSQL ended the save with.</param>
public sealed record SaveDeadlocked(string SqlState);
