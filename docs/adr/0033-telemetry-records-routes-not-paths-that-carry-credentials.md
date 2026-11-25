# 0033: Telemetry records routes, not paths that carry credentials

**Status:** Accepted (#311; the owner chose A + C + E on 2026-11-24)

## Context

- Certification's public verification, `GET /certification/verify/{code}` (#51, ADR 0029), takes a bearer credential in its path: whoever holds the code reads the certificate.
- The path reached three places that leave the process:
  - the edge's spans: its server span's `url.path`, and the `url.full` of the call it forwards;
  - the service's server span, in `url.path`;
  - every exported log written during the request. ServiceDefaults exports scopes (`IncludeScopes`), and ASP.NET Core hosting opens a scope holding `RequestPath` around each request.
- The edge knows no module's routes (AGENTS.md, services topology), so it cannot tell a path that carries a credential from any other.

## Decision

- **A. The edge records the module, not the path.** Its server span's `url.path` keeps `/api` where the call came under it, and the first segment after it: `/api/certification/…`. Every call it makes keeps its first segment in `url.full`: `http://certification/certification/…`. The rule is `AddPathRedactedTelemetry` (`src/Edge/Wiring/TelemetryRedaction.cs`). The service's own span, in the same trace, says which route it was.
- **C. A route marked `RedactedRoute` records its template.**
  - `RedactedRoute` is plain endpoint metadata in the Kernel, so a module's Api and ServiceDefaults both see it and nothing new is referenced.
  - ServiceDefaults sets such a route's span `url.path` to the route's template. A request no route took (no endpoint, or ASP.NET Core's 404 or 405, as for a `HEAD` or an extra segment on a marked route) records its first segment alone, since its path may still carry the credential.
  - Certification's verification is marked. A module marks any further route whose path carries a credential the same way, where it is mapped: `.WithMetadata(new RedactedRoute())`.
- **E. A request's log scope holds the route's template.**
  - ServiceDefaults never opens hosting's request scope: it opens only while `Microsoft.AspNetCore.Hosting.Diagnostics` logs, so that category is set to `None` in code.
  - In its place, a startup filter opens `RequestLogScope` first in every pipeline. It holds `RequestId` and `RequestPath`.
  - `RequestPath` is the template of the route that took the request, for every route and not only marked ones; it is read when a log reads the scope, once routing has chosen the route. A request no route took shows its first segment alone. When the request ends the value is kept, for an exporter that reads scopes later.

## Consequences

- The verification code appears in no span, log or scope that is exported, from the edge or from Certification (`EdgeTelemetryTests`, `RequestTelemetryTests`, `VerifyCertificateEndpointTests`).
- Every edge span loses the path after the module, for every request. Following one request means following its trace into the service, whose span names the route.
- No log's `RequestPath` holds a concrete path any more, only the route's template. An id that a log needs must be logged as a value of its own, as the modules already do.
- Hosting's own logs (category `Microsoft.AspNetCore.Hosting.Diagnostics`) are off. At the level every host logs it (`Warning`), it wrote nearly nothing; the request's start and end are in the spans.
- A route whose path carries a credential and is not marked keeps its path in its span. Marking it is the module's part.
