using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>
/// The realm the published Keycloak imports once, on its first start (B10 on #114, docs/adr/0005):
/// <c>src/AppHost/keycloak/realm.json</c>. It is the development realm, <c>src/AppHost/realms/realm.json</c>, without its
/// local users and with registration closed until #158, email verified, TLS required from outside, brute-force detection on,
/// mail through the deployment's SMTP server and the web client bound to the web app's origin, <c>${WEB_ORIGIN}</c>, without
/// the password grant (K1 and X4 of the PR-C plan). Nothing else may differ, so a role, client, mapper or required action
/// added to one realm and not the other fails here. Read as plain JSON; Keycloak replaces the <c>${...}</c> placeholders
/// from its environment when it imports the file.
/// </summary>
public class PublishedRealmTests
{
    private const string WebClient = "pharma-training-web";

    // Every path below may differ between the two realms; K1 pins what the published realm holds at each.
    private static readonly string[] RealmDifferences = ["users", "registrationAllowed", "verifyEmail", "sslRequired", "bruteForceProtected"];

    private static readonly string[] MailDifferences = ["host", "from", "user", "password", "port", "starttls", "ssl", "auth"];

    private static readonly string[] WebClientDifferences = ["redirectUris", "webOrigins", "directAccessGrantsEnabled"];

    [Fact]
    public void The_published_realm_has_no_users()
    {
        var realm = Published();

        realm.AsObject().ContainsKey("users").ShouldBeFalse(
            $"src/AppHost/keycloak/realm.json holds users: {realm["users"]?.ToJsonString()}. The local users and their passwords are for development alone; a deployment's first editor and manager are created with kcadm.sh.");
    }

    [Fact]
    public void Registration_stays_closed_and_email_must_be_verified()
    {
        var realm = Published();

        Value(realm, "registrationAllowed").ShouldBe("false", "The published realm must keep registrationAllowed false until #158 settles brute-force detection, bot protection and enumeration.");
        Value(realm, "verifyEmail").ShouldBe("true", "The published realm must have verifyEmail true.");
        Value(realm, "sslRequired").ShouldBe("external", "The published realm must have sslRequired \"external\".");
        Value(realm, "bruteForceProtected").ShouldBe("true", "The published realm must have bruteForceProtected true.");
        var verifyEmail = (realm["requiredActions"] as JsonArray ?? [])
            .SingleOrDefault(action => (string?)action?["alias"] == "VERIFY_EMAIL");
        verifyEmail.ShouldNotBeNull("The published realm has no required action VERIFY_EMAIL.");
        Value(verifyEmail, "enabled").ShouldBe("true", "VERIFY_EMAIL must be enabled in the published realm.");
        Value(verifyEmail, "defaultAction").ShouldBe("true", "VERIFY_EMAIL must be a default action in the published realm.");
    }

    [Fact]
    public void The_web_client_accepts_the_web_origin_alone_and_no_password_grant()
    {
        var client = Client(Published(), WebClient);

        client.ShouldNotBeNull($"The published realm has no client {WebClient}.");
        Value(client, "redirectUris").ShouldBe("""["${WEB_ORIGIN}/*"]""", $"{WebClient} must redirect to ${{WEB_ORIGIN}}/* alone.");
        Value(client, "webOrigins").ShouldBe("""["${WEB_ORIGIN}"]""", $"{WebClient} must accept ${{WEB_ORIGIN}} alone as a web origin.");
        Value(client, "directAccessGrantsEnabled").ShouldBe("false", $"{WebClient} must not accept the password grant once published; it exists for local tests alone.");
    }

    [Fact]
    public void Mail_goes_through_the_deployments_smtp_server_with_starttls()
    {
        var mail = Published()["smtpServer"];

        mail.ShouldNotBeNull("The published realm has no smtpServer.");
        var actual = MailDifferences.ToDictionary(key => key, key => Value(mail, key), StringComparer.Ordinal);
        actual.ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = "${SMTP_HOST}",
                ["from"] = "${SMTP_FROM}",
                ["user"] = "${SMTP_USERNAME}",
                ["password"] = "${SMTP_PASSWORD}",
                ["port"] = "587",
                ["starttls"] = "true",
                ["ssl"] = "false",
                ["auth"] = "true",
            },
            "The published realm's smtpServer must name the deployment's SMTP server through Keycloak's environment, on port 587 with STARTTLS and authentication.");
    }

    [Fact]
    public void Everything_else_equals_the_development_realm()
    {
        var published = WithoutTheDifferences(Published());
        var development = WithoutTheDifferences(Development());

        List<string> differences = [];
        Compare("$", development, published, differences);

        differences.ShouldBeEmpty(
            "src/AppHost/keycloak/realm.json differs from src/AppHost/realms/realm.json beyond the differences K1 allows (users, registrationAllowed, verifyEmail, sslRequired, bruteForceProtected, smtpServer's server, port and security, and pharma-training-web's redirectUris, webOrigins and directAccessGrantsEnabled).");
    }

    private static JsonNode Published() => Read("src/AppHost/keycloak/realm.json");

    private static JsonNode Development() => Read("src/AppHost/realms/realm.json");

    private static JsonNode Read(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        File.Exists(path).ShouldBeTrue($"{relativePath} does not exist.");
        return JsonNode.Parse(File.ReadAllText(path)) ?? throw new ShouldAssertException($"{relativePath} is empty.");
    }

    /// <summary>A copy of a realm without the paths K1 lets differ.</summary>
    private static JsonObject WithoutTheDifferences(JsonNode realm)
    {
        var copy = realm.DeepClone().AsObject();
        foreach (var key in RealmDifferences)
        {
            copy.Remove(key);
        }

        if (copy["smtpServer"] is JsonObject mail)
        {
            foreach (var key in MailDifferences)
            {
                mail.Remove(key);
            }
        }

        if (Client(copy, WebClient) is JsonObject client)
        {
            foreach (var key in WebClientDifferences)
            {
                client.Remove(key);
            }
        }

        return copy;
    }

    private static JsonObject? Client(JsonNode realm, string clientId) =>
        (realm["clients"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(client => (string?)client["clientId"] == clientId);

    /// <summary>A property as JSON text, strings unquoted; "(missing)" where there is none.</summary>
    private static string Value(JsonNode node, string property) =>
        node[property] switch
        {
            null => "(missing)",
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            var other => other.ToJsonString(),
        };

    /// <summary>Every path at which two JSON values differ, with both values.</summary>
    private static void Compare(string path, JsonNode? development, JsonNode? published, List<string> differences)
    {
        switch (development, published)
        {
            case (JsonObject left, JsonObject right):
                foreach (var key in left.Select(property => property.Key).Union(right.Select(property => property.Key)).Order(StringComparer.Ordinal))
                {
                    if (!right.ContainsKey(key))
                    {
                        differences.Add($"{path}.{key} is missing from the published realm (development: {left[key]?.ToJsonString()})");
                    }
                    else if (!left.ContainsKey(key))
                    {
                        differences.Add($"{path}.{key} is in the published realm alone: {right[key]?.ToJsonString()}");
                    }
                    else
                    {
                        Compare($"{path}.{key}", left[key], right[key], differences);
                    }
                }

                break;
            case (JsonArray left, JsonArray right) when left.Count == right.Count:
                for (var index = 0; index < left.Count; index++)
                {
                    Compare($"{path}[{index}]", left[index], right[index], differences);
                }

                break;
            default:
                if (!JsonNode.DeepEquals(development, published))
                {
                    differences.Add($"{path}: development {development?.ToJsonString() ?? "null"}, published {published?.ToJsonString() ?? "null"}");
                }

                break;
        }
    }

    /// <summary>The project root: the directory above the test output that holds build.proj, as WorkflowActionsArePinned finds it.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.proj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("build.proj not found above the test output directory.");
    }
}
