using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// Access tokens as Keycloak issues them for the API (issuer the edge's authority, audience <c>pharma-training-api</c>),
/// signed with a key made for this test run alone, which the edge is told to trust instead of fetching Keycloak's.
/// </summary>
public static class TestTokens
{
    private const string Audience = "pharma-training-api";

    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32));

    /// <summary>Makes the edge's JWT bearer handler trust the test's key, with no identity provider to reach.</summary>
    public static void Trust(JwtBearerOptions options)
    {
        Ensure.That(options).IsNotNull();

        var configuration = new OpenIdConnectConfiguration { Issuer = EdgeHost.Authority };
        configuration.SigningKeys.Add(SigningKey);
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        options.TokenValidationParameters.ValidIssuer = EdgeHost.Authority;
        options.TokenValidationParameters.IssuerSigningKey = SigningKey;
    }

    /// <summary>An access token for a user of the realm, by name.</summary>
    public static string For(string username) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = EdgeHost.Authority,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim("preferred_username", username)]),
            Expires = TimeProvider.System.GetUtcNow().AddMinutes(5).UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
}
