using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>Reads the fields of the services', Keycloak's and Mailpit's JSON answers, and writes request bodies.</summary>
internal static class Json
{
    /// <summary>A string field, or null when it is absent or null.</summary>
    public static string? Text(JsonNode? node, string name) => node?[name] switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        JsonValue value when value.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToJsonString(),
        _ => null,
    };

    /// <summary>A string field the answer must have.</summary>
    public static string Required(JsonNode? node, string name, string what) =>
        Text(node, name) ?? throw new SeedingException($"{what} answered without '{name}'.");

    /// <summary>A number field, written as a number or a string, or 0 when it is absent.</summary>
    public static int Int(JsonNode? node, string name) => node?[name] switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => value.GetValue<int>(),
        JsonValue value when value.GetValueKind() == JsonValueKind.String => int.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture),
        _ => 0,
    };

    public static bool Bool(JsonNode? node, string name) => node?[name] is JsonValue value && value.GetValueKind() == JsonValueKind.True;

    /// <summary>The elements of an array field, or none.</summary>
    public static IReadOnlyList<JsonNode> Items(JsonNode? node, string name) =>
        node?[name] is JsonArray array ? [.. array.OfType<JsonNode>()] : [];

    public static StringContent Content(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
}
