using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

public class OpenApiDocumentTests(AspireFixture aspire)
{
    private const string ComponentSchemas = "#/components/schemas/";

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

    /// <summary>
    /// An upload is for a PDF or a video; a text chapter takes no file. The document the frontend's client is generated
    /// from names only the kinds the endpoint accepts, so the client cannot offer Text where it is always refused.
    /// </summary>
    [Fact]
    public async Task The_editorial_upload_requests_kind_allows_exactly_pdf_and_video()
    {
        var served = await ServedAsync();
        var body = Resolve(served, served["paths"]?["/editorial/course-versions/{id}/uploads"]?["post"]?["requestBody"]?["content"]?["application/json"]?["schema"]);

        var kind = Resolve(served, body["properties"]?["kind"]);

        var values = kind["enum"]?.AsArray().Select(value => value?.GetValue<string>()).ToList();
        values.ShouldNotBeNull($"the upload request's kind is an enum: {kind.ToJsonString()}");
        values.ShouldBe(["Pdf", "Video"], ignoreOrder: true);
    }

    /// <summary>
    /// A chapter's body may leave out what its kind does not take: a text chapter has no file or duration, a PDF no text
    /// or duration, and the endpoint accepts each without them. The document requires only what the endpoint requires.
    /// </summary>
    [Fact]
    public async Task The_editorial_chapter_request_requires_its_title_and_none_of_its_optional_content()
    {
        var served = await ServedAsync();
        var chapter = Resolve(served, served["components"]?["schemas"]?["EditorialChapterRequest"]);

        var required = chapter["required"]?.AsArray().Select(name => name?.GetValue<string>()).ToList() ?? [];

        required.ShouldContain("title");
        required.ShouldNotContain("text", $"a pdf or video chapter is sent without text; required: [{string.Join(", ", required)}]");
        required.ShouldNotContain("file", $"a text chapter is sent without a file; required: [{string.Join(", ", required)}]");
        required.ShouldNotContain("durationSeconds", $"only a video has a duration; required: [{string.Join(", ", required)}]");
    }

    private async Task<JsonObject> ServedAsync()
    {
        using var client = aspire.CreateHostClient();
        return JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!.AsObject();
    }

    /// <summary>The schema itself, following a <c>$ref</c> to a component schema where it names one.</summary>
    private static JsonObject Resolve(JsonObject document, JsonNode? schema)
    {
        schema.ShouldNotBeNull("the document has the schema");
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            reference.ShouldStartWith(ComponentSchemas);
            return Resolve(document, document["components"]?["schemas"]?[reference[ComponentSchemas.Length..]]);
        }

        return schema.AsObject();
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
