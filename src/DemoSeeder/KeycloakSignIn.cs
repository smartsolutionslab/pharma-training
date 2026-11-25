using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Signs a user in with the authorization-code flow with PKCE against the realm's web client, without a browser: the
/// login form is posted, the code read from the redirect, and exchanged with the verifier. Answers the access token.
/// </summary>
public sealed class KeycloakSignIn(HttpClient http, Uri authority, string clientId, Uri webOrigin)
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public async Task<string> SignInAsync(string username, string password, CancellationToken cancellationToken)
    {
        var verifier = RandomText();
        var state = RandomText();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirectUri = new Uri(webOrigin, "/").ToString();
        var realm = authority.ToString().TrimEnd('/');

        var authorize = new Uri(
            $"{realm}/protocol/openid-connect/auth?client_id={Uri.EscapeDataString(clientId)}&response_type=code&scope=openid"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}&code_challenge={challenge}&code_challenge_method=S256");
        string action;
        string cookies;
        using (var page = await http.GetAsync(authorize, cancellationToken))
        {
            var html = await page.Content.ReadAsStringAsync(cancellationToken);
            action = page.IsSuccessStatusCode ? FormAction(html) ?? string.Empty : string.Empty;
            if (action.Length == 0)
            {
                throw new SeedingException($"Signing {username} in: the authorization endpoint answered {(int)page.StatusCode} with the page '{Title(html)}' and no login form.");
            }

            cookies = Cookies(page);
        }

        using var login = new HttpRequestMessage(HttpMethod.Post, new Uri(authority, WebUtility.HtmlDecode(action)))
        {
            Content = new FormUrlEncodedContent([new("username", username), new("password", password), new("credentialId", string.Empty)]),
        };
        login.Headers.Add("Cookie", cookies);
        var location = await RedirectAsync(login, username, cookies, cancellationToken);
        var redirect = Pairs(location.Query);
        if (!location.AbsoluteUri.StartsWith(redirectUri, StringComparison.Ordinal)
            || redirect.GetValueOrDefault("state") != state
            || redirect.GetValueOrDefault("code") is not { Length: > 0 } code)
        {
            throw new SeedingException($"Signing {username} in: the login form redirected to {location.GetLeftPart(UriPartial.Path)} without a code for this sign-in.");
        }

        using var form = new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("client_id", clientId),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("code_verifier", verifier),
        ]);
        using var exchange = await http.PostAsync(new Uri($"{realm}/protocol/openid-connect/token"), form, cancellationToken);
        if (!exchange.IsSuccessStatusCode)
        {
            throw new SeedingException($"Signing {username} in: the token endpoint answered {(int)exchange.StatusCode} to the code.");
        }

        var token = JsonNode.Parse(await exchange.Content.ReadAsStringAsync(cancellationToken));
        return Json.Text(token, "access_token") ?? throw new SeedingException($"Signing {username} in: the token endpoint answered without an access token.");
    }

    /// <summary>A query string's names and values, decoded.</summary>
    internal static Dictionary<string, string> Pairs(string query)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? part : part[..separator];
            var value = separator < 0 ? string.Empty : part[(separator + 1)..];
            pairs[Uri.UnescapeDataString(name.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return pairs;
    }

    /// <summary>
    /// Posts the login form and answers where it leads outside Keycloak: a redirect that stays on Keycloak's origin (a step
    /// of the login flow) is followed with the cookies so far; a page instead of a redirect fails, naming its title.
    /// </summary>
    private async Task<Uri> RedirectAsync(HttpRequestMessage login, string username, string cookies, CancellationToken cancellationToken)
    {
        const int MostSteps = 5;
        var request = login;
        for (var step = 0; step < MostSteps; step++)
        {
            using var answer = await http.SendAsync(request, cancellationToken);
            if (!ReferenceEquals(request, login))
            {
                request.Dispose();
            }

            if ((int)answer.StatusCode is < 300 or > 399 || answer.Headers.Location is null)
            {
                var html = await answer.Content.ReadAsStringAsync(cancellationToken);
                throw new SeedingException($"Signing {username} in: the login form answered {(int)answer.StatusCode} with the page '{Title(html)}' instead of the redirect to the web app.");
            }

            var location = answer.Headers.Location.IsAbsoluteUri ? answer.Headers.Location : new Uri(authority, answer.Headers.Location);
            if (location.GetLeftPart(UriPartial.Authority) != authority.GetLeftPart(UriPartial.Authority))
            {
                return location;
            }

            cookies = Merge(cookies, Cookies(answer));
            request = new HttpRequestMessage(HttpMethod.Get, location);
            request.Headers.Add("Cookie", cookies);
        }

        request.Dispose();
        throw new SeedingException($"Signing {username} in: the login form redirected within Keycloak more than {MostSteps} times.");
    }

    /// <summary>43 characters of base64url from 32 random bytes: a PKCE verifier (RFC 7636) or a state.</summary>
    private static string RandomText() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? FormAction(string html)
    {
        var form = Regex.Match(html, "<form\\b[^>]*\\baction=\"([^\"]+)\"", RegexOptions.IgnoreCase, MatchTimeout);
        return form.Success ? form.Groups[1].Value : null;
    }

    /// <summary>The page's title, which a failure names instead of the page's content.</summary>
    private static string Title(string html)
    {
        var title = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, MatchTimeout);
        return title.Success ? WebUtility.HtmlDecode(title.Groups[1].Value).Trim() : "(untitled)";
    }

    /// <summary>The cookies of a Cookie header with those a later answer set, a later value replacing an earlier one.</summary>
    private static string Merge(string cookies, string set)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cookie in $"{cookies}; {set}".Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = cookie.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                merged[cookie[..separator]] = cookie;
            }
        }

        return string.Join("; ", merged.Values);
    }

    /// <summary>The cookies the login page set, as one Cookie header: the handler keeps none of its own.</summary>
    private static string Cookies(HttpResponseMessage page) =>
        page.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(value => value.Split(';', 2)[0].Trim()).Where(cookie => cookie.Contains('=', StringComparison.Ordinal)))
            : string.Empty;
}
