using System.Net;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// A limit a module registers by <see cref="Name"/> with <c>AddUserRateLimit</c> and names on an endpoint with
/// <c>RequireUserRateLimit</c>: every one of its <see cref="Windows"/> per caller, and the <see cref="Refusal"/>
/// (at 429) a caller over it gets (docs/adr/0020).
/// </summary>
public sealed class UserRateLimit
{
    private UserRateLimit(string name, ApiError refusal, IReadOnlyList<UserRateWindow> windows)
    {
        Name = name;
        Refusal = refusal;
        Windows = windows;
    }

    public string Name { get; }

    public ApiError Refusal { get; }

    public IReadOnlyList<UserRateWindow> Windows { get; }

    public static UserRateLimit Of(string name, ApiError refusal, params IReadOnlyList<UserRateWindow> windows)
    {
        Ensure.That(name).IsNotNullOrWhiteSpace();
        Ensure.That(refusal).IsNotNull();
        Ensure.That((int)refusal.Status, nameof(refusal)).Satisfies(status => status == (int)HttpStatusCode.TooManyRequests, $"a limit refuses with 429 Too Many Requests; {refusal.Code} is {refusal.Status}.");
        Ensure.That(windows).IsNotNull();
        Ensure.That(windows.Count, nameof(windows)).Satisfies(count => count > 0, "a limit has at least one window.");
        return new UserRateLimit(name, refusal, [.. windows]);
    }
}
