using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>The stored event is announced as the handler that stored it would have announced it, with its marker.</summary>
/// <param name="Announcement">The announcement, mapped as the handler that stored the event maps it.</param>
public sealed record AnnounceSweptEvent(IntegrationEvent Announcement);
