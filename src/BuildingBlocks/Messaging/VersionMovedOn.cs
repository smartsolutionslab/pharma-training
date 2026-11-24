namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The row version of an aggregate the save changed moved on since it was loaded: another save changed it first, so
/// the caller works from what it no longer is and has to load it again before it repeats the change.
/// </summary>
public sealed record VersionMovedOn;
