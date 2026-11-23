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
    /// limit's refusal, and the endpoint does not run (docs/adr/0020). A name no module registered fails when the
    /// endpoints are built, not per request.
    /// </summary>
    public static RouteHandlerBuilder RequireUserRateLimit(this RouteHandlerBuilder builder, string name)
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

            var filter = new UserRateLimitFilter(
                registered.Value,
                services.GetRequiredService<UserRateLimiter>(),
                services.GetRequiredService<ILogger<UserRateLimitFilter>>());
            return invocation => filter.InvokeAsync(invocation, next);
        });
        return builder.ProducesProblem(StatusCodes.Status429TooManyRequests);
    }
}
