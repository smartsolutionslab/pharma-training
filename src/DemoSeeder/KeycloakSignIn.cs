namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Signs a user in with the authorization-code flow with PKCE against the realm's web client, without a browser: the
/// login form is posted, the code read from the redirect, and exchanged with the verifier. Answers the access token.
/// </summary>
public sealed class KeycloakSignIn(HttpClient http, Uri authority, string clientId, Uri webOrigin)
{
    // stub for #457
    public Task<string> SignInAsync(string username, string password, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"stub for #457 ({http}, {authority}, {clientId}, {webOrigin}, {username}, {password.Length}, {cancellationToken})");
}
