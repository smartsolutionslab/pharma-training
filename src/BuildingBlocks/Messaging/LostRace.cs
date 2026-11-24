namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// How a save through <c>ModuleUnitOfWork.TryCommitAsync</c> lost a race, having saved and sent nothing: an aggregate's
/// row version moved on since it was loaded, or PostgreSQL ended the save as a deadlock, a serialization failure or a
/// lock timeout. A module maps each to its own error with a switch without a discard arm, so a new case fails the build
/// until it is.
/// </summary>
public union LostRace(VersionMovedOn, SaveDeadlocked);
