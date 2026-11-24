using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>
/// The caller an anonymous endpoint's limit counts is its address (docs/adr/0026): the rightmost <c>X-Forwarded-For</c>
/// entry, which the edge sets to the address it saw, else the connection's own address, prefixed <c>address:</c> so it
/// never meets a token's <c>sub</c>. An IPv4-mapped address counts as its IPv4 address and an IPv6 address as its /64,
/// which one client holds whole. A caller with no address at all shares <c>address:unknown</c> with every other.
/// </summary>
public class ClientRateSubjectTests
{
    private static readonly IPAddress Edge = IPAddress.Parse("192.0.2.1");

    [Fact]
    public void A_single_forwarded_address_is_the_subject()
    {
        var context = Request(Edge, "203.0.113.9");

        ClientRateSubject.Of(context).ShouldBe("address:203.0.113.9");
    }

    [Fact]
    public void Of_several_forwarded_addresses_the_rightmost_is_the_subject()
    {
        var context = Request(Edge, "203.0.113.9 ,  198.51.100.7");

        ClientRateSubject.Of(context).ShouldBe("address:198.51.100.7");
    }

    [Fact]
    public void Of_several_forwarded_headers_the_rightmost_entry_is_the_subject()
    {
        var context = Request(Edge, "203.0.113.9", "198.51.100.7");

        ClientRateSubject.Of(context).ShouldBe("address:198.51.100.7");
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData("203.0.113.9, ")]
    [InlineData("203.0.113.9, unknown")]
    public void An_unreadable_forwarded_address_falls_back_to_the_connections_address(string forwarded)
    {
        var context = Request(Edge, forwarded);

        ClientRateSubject.Of(context).ShouldBe("address:192.0.2.1");
    }

    [Fact]
    public void Without_a_forwarded_address_the_connections_address_is_the_subject()
    {
        var context = Request(Edge);

        ClientRateSubject.Of(context).ShouldBe("address:192.0.2.1");
    }

    [Fact]
    public void Without_any_address_every_caller_shares_the_unknown_subject()
    {
        var context = Request(remote: null);

        ClientRateSubject.Of(context).ShouldBe("address:unknown");
    }

    [Fact]
    public void An_ipv4_mapped_ipv6_address_counts_as_its_ipv4_address()
    {
        ClientRateSubject.Of(Request(Edge, "::ffff:203.0.113.9")).ShouldBe("address:203.0.113.9");
        ClientRateSubject.Of(Request(IPAddress.Parse("::ffff:198.51.100.7"))).ShouldBe("address:198.51.100.7");
    }

    [Fact]
    public void Two_ipv6_addresses_in_one_slash_64_are_one_subject()
    {
        var first = ClientRateSubject.Of(Request(Edge, "2001:db8:1:2::1"));
        var second = ClientRateSubject.Of(Request(Edge, "2001:db8:1:2:ffff:ffff:ffff:5"));
        var fromConnection = ClientRateSubject.Of(Request(IPAddress.Parse("2001:db8:1:2:abcd::9")));

        first.ShouldStartWith("address:");
        second.ShouldBe(first);
        fromConnection.ShouldBe(first);
    }

    [Fact]
    public void Ipv6_addresses_in_different_slash_64s_are_different_subjects()
    {
        var first = ClientRateSubject.Of(Request(Edge, "2001:db8:1:2::1"));
        var second = ClientRateSubject.Of(Request(Edge, "2001:db8:1:3::1"));

        second.ShouldNotBe(first);
    }

    [Fact]
    public void A_tokens_subject_never_changes_the_address_subject()
    {
        var anonymous = Request(Edge, "203.0.113.9");
        var signedIn = Request(Edge, "203.0.113.9");
        signedIn.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "0198c1a0-0000-7000-8000-000000000001")], authenticationType: "Bearer"));

        ClientRateSubject.Of(signedIn).ShouldBe(ClientRateSubject.Of(anonymous));
        ClientRateSubject.Of(signedIn).ShouldBe("address:203.0.113.9");
    }

    private static DefaultHttpContext Request(IPAddress? remote, params string[] forwarded)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote;
        if (forwarded.Length > 0)
        {
            context.Request.Headers["X-Forwarded-For"] = forwarded;
        }

        return context;
    }
}
