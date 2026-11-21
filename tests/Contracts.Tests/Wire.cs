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
        ShouldCarryEveryParameter(typeof(TEvent), JsonElement.Parse(json));

        return json;
    }

    /// <summary>
    /// Every value a record is built from is on the wire under its own name, at every depth, so a member the serializer
    /// skips (<c>[JsonIgnore]</c>, a getter it cannot see) fails here rather than reaching consumers as its default.
    /// </summary>
    private static void ShouldCarryEveryParameter(Type type, JsonElement element)
    {
        var constructor = type.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();
        foreach (var parameter in constructor.GetParameters())
        {
            var name = Options.PropertyNamingPolicy?.ConvertName(parameter.Name ?? string.Empty) ?? parameter.Name ?? string.Empty;
            element.TryGetProperty(name, out var value).ShouldBeTrue($"{type.Name}.{parameter.Name} is not on the wire as \"{name}\".");

            var elementType = parameter.ParameterType.IsGenericType ? parameter.ParameterType.GetGenericArguments()[0] : null;
            if (elementType is { IsClass: true } && elementType != typeof(string) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    ShouldCarryEveryParameter(elementType, item);
                }
            }
        }
    }
}
