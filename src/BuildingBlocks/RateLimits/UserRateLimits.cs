namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// The limits the host's modules registered with <c>AddUserRateLimit</c>, by name; one instance per service collection,
/// so a name is registered once across every module (docs/adr/0020).
/// </summary>
public sealed class UserRateLimits
{
    private readonly Dictionary<string, UserRateLimit> limits = [with(StringComparer.Ordinal)];

    /// <summary>Adds the limit; a message naming it when its name is registered already, and nothing is added.</summary>
    public Option<string> Register(UserRateLimit limit)
    {
        Ensure.That(limit).IsNotNull();

        if (limits.TryAdd(limit.Name, limit))
        {
            return Option<string>.None;
        }

        return Option<string>.Some($"The rate limit '{limit.Name}' is registered twice; a module registers each of its limits once, under a name of its own.");
    }

    public Option<UserRateLimit> Find(string name)
    {
        Ensure.That(name).IsNotNull();

        if (limits.TryGetValue(name, out var limit))
        {
            return Option<UserRateLimit>.Some(limit);
        }

        return Option<UserRateLimit>.None;
    }
}
