using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

namespace Microsoft.AspNetCore.Builder;

public static class UserRateLimitEndpoints
{
    /// <summary>
    /// Counts every request that reaches the endpoint, after authorization, under the limit a module registered as
    /// <paramref name="name"/> with <c>AddUserRateLimit</c>; a caller over it gets 429 with <c>Retry-After</c> and the
    /// limit's refusal, and the endpoint does not run (docs/adr/0020). A name no module registered does not stop the
    /// host: the check runs in the endpoint filter factory, when routing first builds the endpoints, so the first request
    /// routed to the host (or building its OpenAPI document) fails with an <see cref="InvalidOperationException"/> naming it.
    /// </summary>
    public static RouteHandlerBuilder RequireUserRateLimit(this RouteHandlerBuilder builder, string name) =>
        builder.RequireRateLimit(name, UserRateLimitFilter.PerUser);

    /// <summary>
    /// Builds the filter of the limit registered as <paramref name="name"/> when routing first builds the endpoints, or
    /// fails naming it; shared by the per-user and the per-client limit, which differ only in the caller they count.
    /// </summary>
    internal static RouteHandlerBuilder RequireRateLimit(
        this RouteHandlerBuilder builder,
        string name,
        Func<UserRateLimit, UserRateLimiter, ILogger<UserRateLimitFilter>, UserRateLimitFilter> filterOf)
    {
        Ensure.That(builder).IsNotNull();
        Ensure.That(name).IsNotNullOrWhiteSpace();

        builder.AddEndpointFilterFactory((factoryContext, next) =>
        {
            var services = factoryContext.ApplicationServices;
            var registered = services.GetService<UserRateLimits>()?.Find(name) ?? Option<UserRateLimit>.None;
            if (!registered.HasValue)
            {
                throw new InvalidOperationException($"The endpoint names the rate limit '{name}', which no module registered with AddUserRateLimit.");
            }

            var filter = filterOf(
                registered.Value,
                services.GetRequiredService<UserRateLimiter>(),
                services.GetRequiredService<ILogger<UserRateLimitFilter>>());
            return invocation => filter.InvokeAsync(invocation, next);
        });
        return builder.ProducesProblem(StatusCodes.Status429TooManyRequests);
    }
}
