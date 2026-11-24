using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// E2 and X6: the browser learns its identity provider from the edge at runtime, before it signs in, so one web build serves
/// every environment. <c>GET /config.json</c> answers <c>FrontendConfiguration</c>, <c>{"oidcAuthority":
/// &lt;Authentication:Authority&gt;, "clientId": "pharma-training-web"}</c>, to anyone, under <c>/api</c> too (where the
/// generated client asks), and the OpenAPI document describes it, so the client is generated for it.
/// </summary>
public class FrontendConfigurationTests
{
    [Theory]
    [InlineData("/config.json")]
    [InlineData("/api/config.json")]
    public async Task The_edge_tells_the_browser_its_identity_provider_at_runtime(string path)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Data(path), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"An anonymous GET {path} answered {(int)response.StatusCode}: {body}");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json", $"GET {path} should answer JSON.");
        var configuration = JsonNode.Parse(body) as JsonObject;
        configuration.ShouldNotBeNull($"GET {path} answered no JSON object: {body}");
        string.Join(", ", configuration.Select(member => member.Key).Order(StringComparer.Ordinal)).ShouldBe("clientId, oidcAuthority", $"GET {path} should answer oidcAuthority and clientId alone: {body}");
        configuration["oidcAuthority"]?.GetValue<string>().ShouldBe(EdgeHost.Authority, "oidcAuthority should be the edge's own Authentication:Authority.");
        configuration["clientId"]?.GetValue<string>().ShouldBe("pharma-training-web", "clientId should be the realm's web client.");
    }

    [Fact]
    public async Task The_runtime_configuration_is_described_in_the_openapi_document()
    {
        await using var edge = await EdgeHost.WithoutTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Data("/openapi/v1.json"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "The edge serves its OpenAPI document in Development.");
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = string.Join(", ", document?["paths"]?.AsObject().Select(path => path.Key).Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>());
        var operation = document?["paths"]?["/config.json"]?["get"];
        operation.ShouldNotBeNull($"The OpenAPI document describes no GET /config.json, so no client is generated for it. Its paths: {paths}");
        operation?["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>()
            .ShouldBe("#/components/schemas/FrontendConfiguration", "GET /config.json should answer the schema FrontendConfiguration, a name no module uses.");

        var schema = document?["components"]?["schemas"]?["FrontendConfiguration"];
        schema.ShouldNotBeNull("The OpenAPI document has no schema FrontendConfiguration.");
        string.Join(", ", schema?["properties"]?.AsObject().Select(property => property.Key).Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>()).ShouldBe("clientId, oidcAuthority", "FrontendConfiguration should have oidcAuthority and clientId alone.");
        string.Join(", ", schema?["required"]?.AsArray().Select(name => name?.GetValue<string>()).Order(StringComparer.Ordinal) ?? Enumerable.Empty<string?>()).ShouldBe("clientId, oidcAuthority", "Both of FrontendConfiguration's properties should be required, so the generated type has no optional members.");
        foreach (var property in new[] { "oidcAuthority", "clientId" })
        {
            schema?["properties"]?[property]?["type"]?.ToJsonString().ShouldBe("\"string\"", $"FrontendConfiguration.{property} should be a string that is never null.");
        }
    }
}
