using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

namespace Microsoft.AspNetCore.Builder;

public static class ClientRateLimitEndpoints
{
    /// <summary>
    /// Counts every request that reaches an anonymous endpoint, after authorization, per client address
    /// (<see cref="ClientRateSubject"/>) under the limit a module registered as <paramref name="name"/> with
    /// <c>AddUserRateLimit</c>; a caller over it gets 429 with <c>Retry-After</c> and the limit's refusal, and the
    /// endpoint does not run (docs/adr/0029). A refusal is logged with the limit's name, never the address. A name no
    /// module registered fails with an <see cref="InvalidOperationException"/> naming it when the endpoints are built.
    /// </summary>
    public static RouteHandlerBuilder RequireClientRateLimit(this RouteHandlerBuilder builder, string name) =>
        builder.RequireRateLimit(name, UserRateLimitFilter.PerClient);
}
