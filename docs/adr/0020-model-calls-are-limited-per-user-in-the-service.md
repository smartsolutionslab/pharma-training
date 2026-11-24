# 0020: Model calls are limited per user, in the service, by an endpoint filter

**Status:** Accepted (#174)

## Context

- #174: the endpoints that call the model, `POST …/content-review` (#23) and `POST …/test-draft` (#22), get a limit per signed-in user, so a looping client or a stolen editor token cannot run up the Azure OpenAI bill. A content review can send about 100k input tokens. The owner decided on 2026-11-22: over the limit, `429` with `Retry-After` and a code of the module's own, and no model call; the limits are configuration.
- #158 needs limits on invitation mail and on accepting invitations, and asks for one policy per limit, registered from the module, never a per-module list in a shared file.
- ASP.NET Core's rate limiter needs `app.UseRateLimiter()` in the pipeline. A service host's `Program.cs` stays as generated and has no slot for it.
- The limiters in `System.Threading.RateLimiting` (.NET 11) take no `TimeProvider`, so tests cannot move their windows. `PartitionedRateLimiter.CreateChained` uses up an earlier limiter's permit when a later one refuses.

## Decision

- **A building block, `BuildingBlocks/RateLimits`.** A module registers each limit with `services.AddUserRateLimit(UserRateLimit.Of(name, refusal, windows...))` in its `AddServices`, and names it on the endpoint with `.RequireUserRateLimit(name)`. A name registered twice stops the host at startup, when the module's `AddServices` runs. An endpoint naming an unregistered limit does not stop the host: the check runs in an endpoint filter factory, when routing first builds the endpoints, so the process starts, but the first request routed to it, its health probes included, or building its OpenAPI document, fails with an `InvalidOperationException` naming the limit. Nothing answers from that host until the name is fixed.
- **It is an endpoint filter, so it runs after authorization.** A caller without a token (`401`) or without the policy (`403`) is never counted. Only callers who reached the endpoint are.
- **Per caller and per limit.** The partition is the token's `sub` (else `NameIdentifier`; a principal with neither shares one partition). One user's use never limits another, and one endpoint's never limits another endpoint.
- **Fixed windows on `TimeProvider`**, each anchored at the caller's first admitted request after the previous window ended. Several windows are checked and counted together: an admitted request counts in every window, a refused one in none. Every admitted request counts, whatever the endpoint answers.
- **A fixed window allows up to twice its limit across a window boundary.** A caller who uses the end of one window and the start of the next gets both budgets in a short time: about 19 calls within a second against 10 a minute, or about 400 content reviews within some 40 minutes across a day boundary against 200 a day. The limit holds per window, not over any stretch of its length. If the bound must hold strictly over every minute or day, a sliding window is the change, behind the same `AddUserRateLimit` / `RequireUserRateLimit`.
- **Over the limit:** `429`, `Retry-After` in whole seconds until the latest full window ends, and problem details with the limit's `ApiError` (`title`, `code`). The endpoint delegate does not run.
- **Editorial's values:** for content review and for test draft separately, 10 per minute and 200 per day per editor. These are the defaults in `EditorialModelLimits`, overridden with `Editorial:ModelLimits:{ContentReview|TestDraft}:{PerMinute|PerDay}` (for example `Editorial__ModelLimits__TestDraft__PerDay=50`), read once at startup like `Editorial:FourEyes` (ADR 0007). A value that is not a whole number from 1 stops the host, naming the key. The code is `editorial.model-limit-reached`.

## Consequences

- Counters live in the service's memory. With more than one replica a user's ceiling is the limit times the replicas, and a restart or redeploy starts every user afresh. ADR 0005 runs one replica minimum. If the services are scaled out on purpose, the counters move into the module's database behind the same `AddUserRateLimit` / `RequireUserRateLimit`, without touching an endpoint.
- Memory grows by one small entry per user and limit, bounded by the number of editors. There is no eviction.
- The web app tells the limit from other 429s by its code. It does not show the retry time.
- #158 limits `POST /organisation/invitations/accept` this way; its mail limits are per invitation and per pharmacy and are kept in Organisation's database (docs/adr/0004).
