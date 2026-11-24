# 0030: The ingress is the one trusted forwarded hop

**Status:** Accepted (#292)

## Context

- ADR 0029 limits anonymous endpoints (`GET /certification/verify/{code}`, #51) per client address: the rightmost `X-Forwarded-For` entry a service receives, which the edge sets with YARP's default transform (action `Set`) to the address the edge saw. It left two things open before Certification serves real traffic: the edge seeing the Container Apps ingress as every client, and counters that are never forgotten.
- Deployed, every request crosses an ingress twice. The browser reaches the edge through the environment's external ingress, which appends the address it saw to `X-Forwarded-For`. The edge reaches a service through the environment's internal ingress, which may append the edge's own address. The review of #298 found the second: a service would then count the edge, whatever the edge sent.
- Aspire 13.6.5 sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on every project it publishes. ASP.NET Core's forwarded headers then trust `X-Forwarded-For` and `X-Forwarded-Proto` from any proxy, with a forward limit of one: the rightmost entry becomes the connection's remote address and leaves the header. Nothing in the repository said so, or relied on it knowingly.

## Decision

- **Published, the edge and every service trust exactly one forwarded hop, the ingress in front of them**, and nothing in run mode. The AppHost names it on each, `TrustsTheIngressHop()` (`src/AppHost/Wiring/IngressForwarding.cs`), rather than leaving it to Aspire's default, and `AppHostForwardedHeadersTests` pins it.
- **At the edge** the client's address is the entry the external ingress appended. Whatever the caller wrote before it, one forged entry or a chain, is dropped: YARP replaces the whole header with that one address (`IngressClientAddressTests`).
- **At a service** the internal ingress's entry, if it appended one, is the hop taken off, so the rightmost entry left is the edge's, the client's address; if it appended none, the edge's entry is taken off instead and becomes the remote address, which `ClientRateSubject` falls back to. Either way the service counts the client (`ServiceBehindTheIngressTests`); `ClientRateSubject` itself does not change.
- **Any proxy is trusted** because only an ingress reaches these apps: the edge has the environment's only external HTTP ingress beside the auth proxy, and a service has an internal one alone. Something inside the environment could still send a forged entry straight to a service; it is already inside the boundary.
- **Counters are forgotten once their windows have all ended** (`UserRateLimiter`), at most once a minute, by the request that finds the sweep due. A counter is retired under its own lock before it is removed, so a request already holding it counts in its replacement instead: none is counted twice or lost.

## Consequences

- Each client has a bucket of its own, so one busy verifier no longer locks out every other.
- Memory holds the callers of the longest window, not every caller since start: at Certification's 500-a-day window, about 200 bytes for each address seen in the last day. A client rotating through IPv6 /64s still costs that per prefix per day, bounded by the rate it can send; a cap on the number of callers would refuse new ones, or forget live counts, and is not taken.
- `X-Forwarded-Proto` is trusted the same way, as it already was under Aspire's default: deployed, the apps see the browser's `https`.
- Whether the internal ingress appends its hop is not observed until the first deploy. Both cases count the client; the runbook's check after the first deploy (#129) sees it on the real ingress.
- An Aspire that stopped setting the variable changes nothing, since the AppHost sets it; one that changed what ASP.NET Core does with it fails the edge's and the services' tests.
