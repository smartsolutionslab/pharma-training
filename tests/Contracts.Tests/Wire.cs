using System.Text.Json;
using Wolverine.Runtime.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Tests;

/// <summary>
/// The JSON an integration event travels as: Wolverine's default serializer (<see cref="SystemTextJsonSerializer"/>)
/// with its own default options, camelCase names and case-insensitive reads; src/Host/Wiring/Messaging.cs sets no other.
/// </summary>
public static class Wire
{
    public static JsonSerializerOptions Options { get; } = SystemTextJsonSerializer.DefaultOptions();

    /// <summary>
    /// Publishes the event (fills its metadata), sends it over the wire and back, and checks that nothing was lost:
    /// the JSON of the event read back is the JSON that was sent, and its metadata is the same.
    /// </summary>
    /// <returns>The JSON the event travelled as, for further checks.</returns>
    public static string ShouldSurviveTheWire<TEvent>(TEvent unpublished)
        where TEvent : IntegrationEvent
    {
        Ensure.That(unpublished).IsNotNull();

        var published = unpublished with
        {
            Metadata = new EventMetadata(Guid.CreateVersion7(), new DateTimeOffset(2026, 11, 21, 9, 30, 0, TimeSpan.FromHours(1))),
        };

        var json = JsonSerializer.Serialize(published, Options);
        var back = JsonSerializer.Deserialize<TEvent>(json, Options);

        back.ShouldNotBeNull();
        JsonSerializer.Serialize(back, Options).ShouldBe(json);
        back.Metadata.ShouldBe(published.Metadata);

        return json;
    }
}
