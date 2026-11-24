using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

namespace Microsoft.Extensions.DependencyInjection;

public static class UserRateLimitRegistration
{
    /// <summary>
    /// Registers a module's limit by its name, for its endpoints to name with <c>RequireUserRateLimit</c>, and the one
    /// <see cref="UserRateLimiter"/> every limit of the host counts in (docs/adr/0020). A name registered before stops
    /// the host, naming it.
    /// </summary>
    public static IServiceCollection AddUserRateLimit(this IServiceCollection services, UserRateLimit limit)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(limit).IsNotNull();

        services.TryAddSingleton<UserRateLimiter>();

        // One registry per service collection, shared by every module the host adds.
        if (services.FirstOrDefault(service => service.ServiceType == typeof(UserRateLimits))?.ImplementationInstance is not UserRateLimits limits)
        {
            limits = new UserRateLimits();
            services.AddSingleton(limits);
        }

        var duplicate = limits.Register(limit);
        if (duplicate.HasValue)
        {
            throw new InvalidOperationException(duplicate.Value);
        }

        return services;
    }
}
