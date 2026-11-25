namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>The ways a save loses a race, each as a <see cref="LostRace"/>, so a result names no type argument.</summary>
public static class LostRaces
{
    /// <summary>The row version of an aggregate the save changed moved on since it was loaded.</summary>
    public static LostRace VersionMovedOn() => new VersionMovedOn();

    /// <summary>PostgreSQL ended the save as a deadlock, a serialization failure or a lock timeout, with this SqlState.</summary>
    public static LostRace Deadlocked(string sqlState) => new SaveDeadlocked(sqlState);
}
