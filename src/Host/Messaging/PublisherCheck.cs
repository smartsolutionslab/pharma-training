using Wolverine;

namespace SmartSolutionsLab.PharmaTraining.Host.Messaging;

/// <summary>
/// Middleware before every consumer: an event from the broker that the broker user who published it may not publish
/// (<see cref="BrokerPublishers"/>) fails with <see cref="ForgedPublisherException"/>, which is dead-lettered at once,
/// so no consumer handles it (docs/adr/0011).
/// </summary>
public static class PublisherCheck
{
    public static void Before(Envelope envelope, BrokerPublishers publishers)
    {
        Ensure.That(envelope).IsNotNull();
        Ensure.That(publishers).IsNotNull();

        if (publishers.RefusalOf(envelope) is { } refusal)
        {
            throw new ForgedPublisherException(refusal);
        }
    }
}
