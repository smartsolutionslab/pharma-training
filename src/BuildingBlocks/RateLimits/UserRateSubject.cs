using System.Security.Claims;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// The caller a limit counts: the token's <c>sub</c>, else its name identifier, as the raw string. A principal with
/// neither is the empty subject, which every such principal shares, so none escapes the limit.
/// </summary>
public static class UserRateSubject
{
    private const string Sub = "sub";

    public static string Of(ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        return user.FindFirst(Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
    }
}
