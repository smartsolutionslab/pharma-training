using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

public class OpenApiDocumentTests(AspireFixture aspire)
{
    private const string ComponentSchemas = "#/components/schemas/";

    /// <summary>A property whose name contains one of these, ignoring case, names a solution.</summary>
    private static readonly string[] SolutionWords = ["correct", "solution", "answerkey"];

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

    /// <summary>
    /// A learner's attempt routes (#46) are in the document with their methods, so the correctness check above walks
    /// what they answer and take, and the generated client has them.
    /// </summary>
    [Fact]
    public async Task The_document_describes_the_attempt_routes()
    {
        var paths = (await ServedAsync())["paths"]?.AsObject();
        paths.ShouldNotBeNull("the document has paths");

        string[] described =
        [
            .. new (string Path, string Method)[]
            {
                ("/assessment/examinations/by-session/{sessionId}", "get"),
                ("/assessment/examinations/{id}/attempts", "post"),
                ("/assessment/examinations/{id}/attempts/current/answers/{questionId}", "put"),
                ("/assessment/examinations/{id}/attempts/current/submit", "post"),
            }.Where(route => paths[route.Path]?[route.Method] is null).Select(route => $"{route.Method.ToUpperInvariant()} {route.Path}"),
        ];

        described.ShouldBeEmpty("these attempt routes are missing from the document");
    }

    /// <summary>
    /// The solutions go to Assessment and never to the frontend (docs/domain/README.md): no schema an assessment path
    /// answers or takes, and no component schema of Assessment's, has a property that names correctness, a solution or an
    /// answer key, however deep.
    /// It catches an inline or anonymous type that the architecture rule AssessmentResponsesCarryNoSolutions might miss.
    /// </summary>
    [Fact]
    public async Task No_assessment_schema_names_a_solution_or_correctness()
    {
        var served = await ServedAsync();
        var schemas = served["components"]?["schemas"]?.AsObject();
        var paths = served["paths"]?.AsObject();
        paths.ShouldNotBeNull("the document has paths");
        List<string> solutionNames = [];
        HashSet<string> visited = [];

        var assessmentPaths = paths.Where(path => path.Key.StartsWith("/assessment/", StringComparison.Ordinal)).ToList();
        foreach (var (name, path) in assessmentPaths)
        {
            CollectSolutionNames(served, path, $"paths{name}", solutionNames, visited);
        }

        foreach (var (name, schema) in schemas?.Where(schema => schema.Key.StartsWith("Assessment", StringComparison.Ordinal)) ?? [])
        {
            CollectSolutionNames(served, schema, $"{ComponentSchemas}{name}", solutionNames, visited);
        }

        assessmentPaths.ShouldNotBeEmpty("the document describes Assessment's endpoints");
        solutionNames.ShouldBeEmpty();
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

    /// <summary>
    /// Every property whose name contains "correct", "solution" or "answerkey", ignoring case, in the node and what its
    /// <c>$ref</c>s name, each component once.
    /// </summary>
    internal static void CollectSolutionNames(JsonObject document, JsonNode? node, string path, List<string> found, HashSet<string> visited)
    {
        if (node is JsonObject schema)
        {
            if (schema["$ref"]?.GetValue<string>() is { } reference && reference.StartsWith(ComponentSchemas, StringComparison.Ordinal))
            {
                var name = reference[ComponentSchemas.Length..];
                if (visited.Add(name))
                {
                    CollectSolutionNames(document, document["components"]?["schemas"]?[name], $"{path} -> {reference}", found, visited);
                }
            }

            if (schema["properties"] is JsonObject properties)
            {
                found.AddRange(properties
                    .Where(property => SolutionWords.Any(word => property.Key.Contains(word, StringComparison.OrdinalIgnoreCase)))
                    .Select(property => $"{path}/properties/{property.Key}"));
            }

            foreach (var (key, child) in schema)
            {
                CollectSolutionNames(document, child, $"{path}/{key}", found, visited);
            }
        }
        else if (node is JsonArray items)
        {
            for (var index = 0; index < items.Count; index++)
            {
                CollectSolutionNames(document, items[index], $"{path}/{index}", found, visited);
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
