using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>The secret of an invitation link: 256 random bits, URL-safe, a new one every time.</summary>
public class InvitationTokensTests
{
    [Fact]
    public void A_token_is_43_url_safe_characters() =>
        InvitationTokens.Next().Value.ShouldMatch("^[A-Za-z0-9_-]{43}$");

    [Fact]
    public void A_thousand_tokens_are_a_thousand_different_ones() =>
        Enumerable.Range(0, 1000).Select(_ => InvitationTokens.Next().Value).Distinct(StringComparer.Ordinal).Count().ShouldBe(1000);
}
