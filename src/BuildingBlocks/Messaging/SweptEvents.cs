using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>What a sweep does with a stored event, each as a <see cref="SweptEvent"/>, so a caller names no case type.</summary>
public static class SweptEvents
{
    /// <summary>Announce the stored event with this announcement, and mark it.</summary>
    public static SweptEvent Announce(IntegrationEvent announcement)
    {
        Ensure.That(announcement).IsNotNull();
        return new AnnounceSweptEvent(announcement);
    }

    /// <summary>Mark the stored event and announce nothing: its announcement belongs to another stored event.</summary>
    public static SweptEvent MarkOnly() => new MarkSweptEventOnly();

    /// <summary>Skip the stored event, which cannot be read or mapped for this reason, and leave it without a marker.</summary>
    public static SweptEvent Unreadable(Exception failure)
    {
        Ensure.That(failure).IsNotNull();
        return new UnreadableSweptEvent(failure);
    }
}
