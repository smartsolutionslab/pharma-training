using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

public class OpenApiDocumentTests(AspireFixture aspire)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The frontend's client is generated from web/app/openapi.json; it must describe the API the host serves.</summary>
    [Fact]
    public async Task The_frontends_copy_of_the_openapi_document_is_current()
    {
        using var client = aspire.CreateHostClient();
        var served = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!.AsObject();
        // Where the host happened to listen is not part of the API; the frontend calls it through /api.
        served.Remove("servers");
        var expected = served.ToJsonString(Indented) + "\n";

        var path = Path.Combine(RepositoryRoot(), "web", "app", "openapi.json");
        var actual = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : string.Empty;
        if (actual != expected)
        {
            await File.WriteAllTextAsync(path, expected, TestContext.Current.CancellationToken);
        }

        actual.ShouldBe(expected, "web/app/openapi.json was stale and has been rewritten: commit it and run pnpm codegen in web/app.");
    }

    /// <summary>
    /// A component the frontend's client is generated from is shared by every place that names it, a response's included:
    /// a null among its enum values makes every one of them nullable, so a request's optional value is the property's
    /// nullability, never a value of its enum.
    /// </summary>
    [Fact]
    public async Task No_component_schemas_enum_has_null_among_its_values()
    {
        using var client = aspire.CreateHostClient();
        var served = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!.AsObject();
        var schemas = served["components"]?["schemas"]?.AsObject();
        schemas.ShouldNotBeNull("the document has component schemas");
        List<string> withNull = [];

        foreach (var (name, schema) in schemas)
        {
            CollectEnumsWithNull(schema, $"#/components/schemas/{name}", withNull);
        }

        schemas.Count.ShouldBeGreaterThan(0);
        withNull.ShouldBeEmpty();
    }

    private static void CollectEnumsWithNull(JsonNode? node, string path, List<string> withNull)
    {
        if (node is JsonObject schema)
        {
            if (schema["enum"] is JsonArray values && values.Any(value => value is null))
            {
                withNull.Add($"{path}/enum: [{string.Join(", ", values.Select(value => value?.ToJsonString() ?? "null"))}]");
            }

            foreach (var (key, child) in schema)
            {
                CollectEnumsWithNull(child, $"{path}/{key}", withNull);
            }
        }
        else if (node is JsonArray items)
        {
            for (var index = 0; index < items.Count; index++)
            {
                CollectEnumsWithNull(items[index], $"{path}/{index}", withNull);
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "build.proj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No build.proj above " + AppContext.BaseDirectory);
    }
}
