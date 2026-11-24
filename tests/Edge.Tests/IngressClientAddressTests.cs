using System.Net.Http.Headers;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// The client address the edge hands a service (#292, docs/adr/0030). Published, the edge sits behind the Container Apps
/// ingress, which appends the address it saw to <c>X-Forwarded-For</c>; the edge trusts exactly that one hop
/// (<c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>, which publishing sets: forward limit 1, any proxy) and forwards the
/// address as the service's <c>X-Forwarded-For</c>, in place of the whole list. Whatever the caller wrote before it,
/// one forged entry or a chain of several, never reaches the service. In run mode the edge trusts no hop.
/// </summary>
public class IngressClientAddressTests
{
    private const string Client = "198.51.100.7";

    private static readonly Dictionary<string, string> Published = new(StringComparer.Ordinal) { ["FORWARDEDHEADERS_ENABLED"] = "true" };

    [Fact]
    public async Task Published_the_service_gets_the_address_the_ingress_appended()
    {
        var forwarded = await ForwardedAsync(Published, Client);

        forwarded.ShouldBe(Client);
    }

    [Fact]
    public async Task Published_an_address_the_caller_forged_before_the_ingress_hop_is_dropped()
    {
        var forwarded = await ForwardedAsync(Published, $"203.0.113.9, {Client}");

        forwarded.ShouldBe(Client);
    }

    [Fact]
    public async Task Published_a_chain_of_several_hops_before_the_ingress_hop_is_dropped()
    {
        var forwarded = await ForwardedAsync(Published, $"10.1.2.3, 203.0.113.9, 192.0.2.44, {Client}");

        forwarded.ShouldBe(Client);
    }

    [Fact]
    public async Task In_run_mode_the_edge_trusts_no_forwarded_hop()
    {
        var forwarded = await ForwardedAsync(new Dictionary<string, string>(StringComparer.Ordinal), $"203.0.113.9, {Client}");

        forwarded.ShouldNotContain("203.0.113.9");
        forwarded.ShouldNotContain(Client);
    }

    /// <summary>What the module service received as <c>X-Forwarded-For</c> for a call through the edge with this header.</summary>
    private static async Task<string> ForwardedAsync(IReadOnlyDictionary<string, string> settings, string forwardedFor)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync("Production", settings);
        using var client = edge.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/catalog/courses");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains(ModuleService.MarkerHeader).ShouldBeTrue($"The edge answered {response.StatusCode} itself; the call never reached the module service.");
        return edge.Service.ReceivedForwardedFor.ShouldHaveSingleItem();
    }
}
