using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>How Assessment's announcement sweeps (#277, #307) mark a stored event they settle.</summary>
public static class AnnouncedEventMarking
{
    /// <summary>Adds the marker of <paramref name="stored"/>, keyed on the stream it is stored in and its version there.</summary>
    public static void Mark(this IAnnouncedEvents announced, UnannouncedEvent stored, DateTimeOffset announcedAt)
    {
        Ensure.That(announced).IsNotNull();
        Ensure.That(stored).IsNotNull();

        announced.Add(new AnnouncedEvent { StreamId = stored.StreamId, StreamVersion = stored.StreamVersion, AnnouncedAt = announcedAt });
    }
}
