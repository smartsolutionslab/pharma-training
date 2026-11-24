# 0026: Anonymous endpoints are limited per client address, from the edge's X-Forwarded-For

**Status:** Accepted (#51)

## Context

- #51: `GET /certification/verify/{code}` answers anyone who scans a certificate's QR code, without signing in, and must be rate limited. ADR 0020's limits are per signed-in user (the token's `sub`); an anonymous caller has none.
- A service sees the edge as its remote address. The edge is YARP with its default `X-Forwarded-*` transforms, which **replace** whatever `X-Forwarded-For` the client sent with the address the edge saw, so a client cannot choose its own partition (pinned by an Edge test).
- The topology rules keep module names and per-route lists out of the edge, so the edge cannot know which routes are anonymous; ADR 0020's reasons against ASP.NET Core's rate limiter (no `UseRateLimiter()` slot in a generated `Program.cs`, no `TimeProvider`) still hold.

## Decision

- **The same building block, a second partition key.** `BuildingBlocks/RateLimits` gains `.RequireClientRateLimit(name)`, which uses the limits registered with `AddUserRateLimit` and ADR 0020's filter, windows and `429` answer, but partitions by client address instead of user.
- **The client address** is the rightmost entry of `X-Forwarded-For`, else the connection's remote address, else one shared `unknown` partition. An IPv4-mapped address counts as IPv4; an IPv6 address counts per /64, since one client holds a whole /64.
- **An address is personal data:** a refusal under a client limit logs the limit and the retry time, never the address.
- **Certification's values:** 30 a minute and 500 a day per address, `Certification:VerificationLimits:{PerMinute|PerDay}`, read at startup like Editorial's; a value that is not a whole number from 1 stops the host. Over the limit: `429`, `Retry-After`, code `certification.verification-limit-reached`.

## Consequences

- **Deployed, every caller shares one partition until the edge trusts the ingress.** In Azure Container Apps the edge's remote address is the ingress, so all public verifiers share one 30-a-minute bucket: stricter than intended, never weaker. Trusting exactly one forwarded hop from the ingress (publish mode only) is a separate decision, left to a follow-up and observed under #129. Locally every caller shares one partition too (Aspire's proxy).
- Counters live in memory per replica without eviction (ADR 0020); memory grows by about 100 bytes per distinct address. Revisit with eviction of expired windows if the public page sees heavy traffic.
- The limit bounds load and abuse; guessing is defeated by the 192-bit verification code (#49), not by the limit.
