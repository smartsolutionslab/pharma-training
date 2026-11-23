using System.Security.Claims;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>
/// The caller a limit counts is the token's subject: <c>sub</c>, else the name identifier, as the raw string. A principal
/// with neither shares the one empty subject with every other, so it never escapes the limit.
/// </summary>
public class UserRateSubjectTests
{
    private const string Subject = "0198c1a0-0000-7000-8000-000000000001";
    private const string NameIdentifier = "0198c1a0-0000-7000-8000-000000000002";

    [Fact]
    public void The_subject_is_the_sub_claim()
    {
        UserRateSubject.Of(User(("sub", Subject), (ClaimTypes.NameIdentifier, NameIdentifier))).ShouldBe(Subject);
    }

    [Fact]
    public void Without_sub_the_subject_is_the_name_identifier()
    {
        UserRateSubject.Of(User((ClaimTypes.NameIdentifier, NameIdentifier))).ShouldBe(NameIdentifier);
    }

    [Fact]
    public void Without_either_the_subject_is_empty()
    {
        UserRateSubject.Of(User(("name", "Emma Editor"))).ShouldBe(string.Empty);
        UserRateSubject.Of(new ClaimsPrincipal(new ClaimsIdentity())).ShouldBe(string.Empty);
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer"));
}
