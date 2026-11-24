using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// The caller an anonymous endpoint's limit counts (docs/adr/0026): its address, prefixed <c>address:</c> so it never
/// meets a token's <c>sub</c>. The address is the rightmost <c>X-Forwarded-For</c> entry, which the edge sets to the
/// address it saw, else the connection's own; an IPv4-mapped address counts as its IPv4 address and an IPv6 address as
/// its /64, which one client holds whole. A caller with no readable address shares <c>address:unknown</c>.
/// </summary>
public static class ClientRateSubject
{
    private const string Prefix = "address:";
    private const string ForwardedFor = "X-Forwarded-For";
    private const int Ipv6PrefixBytes = 8;

    public static string Of(HttpContext context)
    {
        Ensure.That(context).IsNotNull();

        var address = Forwarded(context.Request.Headers[ForwardedFor]) ?? context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return Prefix + "unknown";
        }

        return Prefix + Normalised(address);
    }

    private static IPAddress? Forwarded(StringValues headers)
    {
        if (headers.Count == 0)
        {
            return null;
        }

        var last = headers[headers.Count - 1] ?? string.Empty;
        var rightmost = last[(last.LastIndexOf(',') + 1)..].Trim();
        if (IPAddress.TryParse(rightmost, out var address))
        {
            return address;
        }

        return null;
    }

    private static string Normalised(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6PrefixBytes, bytes.Length - Ipv6PrefixBytes);
        return new IPAddress(bytes).ToString() + "/64";
    }
}
