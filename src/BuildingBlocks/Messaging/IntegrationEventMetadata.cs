using SmartSolutionsLab.PharmaTraining.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>How a unit of work fills the metadata of an event it publishes.</summary>
public static class IntegrationEventMetadata
{
    /// <summary>
    /// The event with new metadata: a Guid v7 identifier and the current time. Called once per
    /// publish, before the event is saved, so every delivery of it carries the same metadata.
    /// </summary>
    public static IntegrationEvent Fill(IntegrationEvent integrationEvent, IServiceProvider services)
    {
        Ensure.That(integrationEvent).IsNotNull();
        Ensure.That(services).IsNotNull();

        var time = services.GetRequiredService<TimeProvider>();
        return integrationEvent with { Metadata = new EventMetadata(Guid.CreateVersion7(), time.GetUtcNow()) };
    }
}
