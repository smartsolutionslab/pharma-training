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

    // The confidential client Organisation deletes an erased person's account with (#115 slice 6), and its service account.
    private const string ErasureClient = "pharma-training-erasure";

    private const string ErasureServiceAccount = "service-account-pharma-training-erasure";

    // Keycloak's own command-line client, which every realm gets on import unless the realm declares it: public, with the
    // password grant on (the security review of #114, PR-C).
    private const string AdminCli = "admin-cli";

    // The longest login and admin events are kept, in seconds: 30 days (docs/adr/0040, #448, #453). A user event names the
    // user, their username and IP address, and an admin event can hold a user's representation, so neither may outlive what
    // security monitoring needs, nor an erased account (#115).
    private const int LongestEventsExpiration = 30 * 24 * 60 * 60;

    // The most failed logins brute-force detection lets one user have before it locks them out for a while.
    private const int MostFailures = 10;

    // Every path below may differ between the two realms; K1 and the security review of #114 pin what the published realm
    // holds at each.
    private static readonly string[] RealmDifferences =
    [
        "users", "registrationAllowed", "verifyEmail", "sslRequired", "bruteForceProtected",
        "failureFactor", "eventsEnabled", "enabledEventTypes", "adminEventsEnabled", "adminEventsDetailsEnabled",
    ];

    // Clients the published realm may declare and the development realm need not: admin-cli, pinned below.
    private static readonly string[] PublishedClients = [AdminCli];

    private static readonly string[] MailDifferences = ["host", "from", "user", "password", "port", "starttls", "ssl", "auth"];

    private static readonly string[] ErasureClientSettings =
    [
        "enabled", "publicClient", "clientAuthenticatorType", "secret", "serviceAccountsEnabled", "standardFlowEnabled",
        "implicitFlowEnabled", "directAccessGrantsEnabled",
    ];

    private static readonly string[] WebClientDifferences = ["redirectUris", "webOrigins", "directAccessGrantsEnabled"];

    /// <summary>
    /// #115 slice 6: the local users and their passwords are for development alone, and a deployment's first editor and
    /// manager are created with kcadm.sh. The one user the published realm holds is the erasure client's service account,
    /// which an import can grant its client role only through a user entry; it has no credentials.
    /// </summary>
    [Fact]
    public void The_published_realm_has_no_user_but_the_erasure_clients_service_account()
    {
        var users = Published()["users"] as JsonArray ?? [];

        var user = users.OfType<JsonObject>().ShouldHaveSingleItem(
            $"src/AppHost/keycloak/realm.json should hold exactly one user, {ErasureServiceAccount}; it holds: {users.ToJsonString()}.");
        Value(user, "username").ShouldBe(ErasureServiceAccount);
        Value(user, "serviceAccountClientId").ShouldBe(ErasureClient);
        user.ContainsKey("credentials").ShouldBeFalse($"{ErasureServiceAccount} must have no credentials in the published realm.");
    }

    /// <summary>
    /// #115 slice 6 (decision D): Organisation deletes an erased person's account as the confidential client
    /// <c>pharma-training-erasure</c>, declared in both realms with its secret from Keycloak's environment, which the
    /// AppHost sets from the secret parameter <c>keycloak-erasure-client-secret</c>. It signs no one in.
    /// </summary>
    [Theory]
    [InlineData("src/AppHost/keycloak/realm.json")]
    [InlineData("src/AppHost/realms/realm.json")]
    public void Both_realms_declare_the_erasure_client_confidential_with_its_secret_from_the_environment(string relativePath)
    {
        var client = Client(Read(relativePath), ErasureClient);

        client.ShouldNotBeNull($"{relativePath} has no client {ErasureClient}.");
        var actual = ErasureClientSettings.ToDictionary(key => key, key => Value(client, key), StringComparer.Ordinal);
        actual.ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["enabled"] = "true",
                ["publicClient"] = "false",
                ["clientAuthenticatorType"] = "client-secret",
                ["secret"] = "${KEYCLOAK_ERASURE_CLIENT_SECRET}",
                ["serviceAccountsEnabled"] = "true",
                ["standardFlowEnabled"] = "false",
                ["implicitFlowEnabled"] = "false",
                ["directAccessGrantsEnabled"] = "false",
            },
            $"{relativePath}'s {ErasureClient} must be a confidential client with the client credentials grant alone, its secret from Keycloak's environment, never a literal.");
    }

    /// <summary>
    /// #115 slice 6: the erasure client's service account may manage users and list the realm's clients, and nothing
    /// else. It lists clients (<c>query-clients</c>) only to find <c>realm-management</c>'s id, so it can refuse to delete
    /// a delegated realm administrator.
    /// </summary>
    [Theory]
    [InlineData("src/AppHost/keycloak/realm.json")]
    [InlineData("src/AppHost/realms/realm.json")]
    public void Both_realms_give_the_erasure_clients_service_account_manage_users_and_query_clients_alone(string relativePath)
    {
        var users = Read(relativePath)["users"] as JsonArray ?? [];

        var serviceAccounts = users.OfType<JsonObject>().Where(user => user.ContainsKey("serviceAccountClientId")).ToList();

        var account = serviceAccounts.ShouldHaveSingleItem($"{relativePath} should hold exactly one service account, {ErasureServiceAccount}.");
        Value(account, "username").ShouldBe(ErasureServiceAccount);
        Value(account, "enabled").ShouldBe("true");
        Value(account, "serviceAccountClientId").ShouldBe(ErasureClient);
        Value(account, "clientRoles").ShouldBe("""{"realm-management":["manage-users","query-clients"]}""", $"{ErasureServiceAccount} must hold realm-management's manage-users and query-clients alone.");
        account.ContainsKey("realmRoles").ShouldBeFalse($"{ErasureServiceAccount} must hold no realm role.");
        account.ContainsKey("credentials").ShouldBeFalse($"{ErasureServiceAccount} must have no credentials.");
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
        Value(client, "redirectUris").ShouldBe(
            """["${WEB_ORIGIN}","${WEB_ORIGIN}/"]""",
            $"{WebClient} must redirect to the web app's origin exactly, ${{WEB_ORIGIN}} or ${{WEB_ORIGIN}}/, where oidc-client-ts returns (redirect_uri and post_logout_redirect_uri are window.location.origin); a path wildcard would let an open redirect on the edge's origin receive the code.");
        Value(client, "webOrigins").ShouldBe("""["${WEB_ORIGIN}"]""", $"{WebClient} must accept ${{WEB_ORIGIN}} alone as a web origin.");
        Value(client, "directAccessGrantsEnabled").ShouldBe("false", $"{WebClient} must not accept the password grant once published; it exists for local tests alone.");
    }

    [Fact]
    public void No_client_of_the_published_realm_accepts_the_password_grant()
    {
        var realm = Published();
        var clients = (realm["clients"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (Client(realm, AdminCli) is null)
        {
            // Keycloak adds its own admin-cli to a realm that does not declare one: public, with the password grant on.
            clients.Add(new JsonObject { ["clientId"] = AdminCli, ["publicClient"] = true, ["directAccessGrantsEnabled"] = true });
        }

        clients.Where(client => Value(client, "directAccessGrantsEnabled") == "true")
            .Select(client => Value(client, "clientId"))
            .ShouldBeEmpty("These clients of the published realm, as Keycloak imports it, accept the password grant, which takes a user's password at the token endpoint the auth proxy makes public; nothing published signs in that way.");
    }

    [Fact]
    public void The_published_realm_declares_admin_cli_without_the_password_grant()
    {
        var client = Client(Published(), AdminCli);

        client.ShouldNotBeNull($"The published realm does not declare {AdminCli}, so Keycloak creates its own on import: a public client that accepts the password grant, at /realms/pharma-training/protocol/openid-connect/token, which the auth proxy forwards from the internet. Declare it with directAccessGrantsEnabled false.");
        Value(client, "publicClient").ShouldBe("true", $"{AdminCli} must stay a public client, as Keycloak creates it.");
        Value(client, "directAccessGrantsEnabled").ShouldBe("false", $"{AdminCli} must not accept the password grant in the published realm.");
    }

    [Fact]
    public void The_published_realm_records_logins_and_administration()
    {
        var realm = Published();

        Value(realm, "eventsEnabled").ShouldBe("true", "The published realm must record user events (eventsEnabled true), so failed logins can be traced.");
        if (realm["enabledEventTypes"] is JsonArray types)
        {
            types.Select(type => (string?)type).ShouldContain("LOGIN_ERROR", $"The published realm records the event types {types.ToJsonString()}, without LOGIN_ERROR: a failed login leaves no trace.");
        }

        Value(realm, "adminEventsEnabled").ShouldBe("true", "The published realm must record admin events (adminEventsEnabled true), so a change to its users, roles or clients can be traced.");
    }

    [Theory]
    [InlineData("src/AppHost/keycloak/realm.json")]
    [InlineData("src/AppHost/realms/realm.json")]
    public void Login_events_expire_within_the_period_the_adr_records(string relativePath)
    {
        var expiration = Value(Read(relativePath), "eventsExpiration");

        long.TryParse(expiration, System.Globalization.CultureInfo.InvariantCulture, out var seconds).ShouldBeTrue(
            $"{relativePath} sets no eventsExpiration: Keycloak then keeps login events forever, past an erased account (#448, docs/adr/0040).");
        seconds.ShouldBeInRange(1, LongestEventsExpiration, $"{relativePath} keeps login events for {seconds} s; docs/adr/0040 allows at most {LongestEventsExpiration} s (30 days).");
    }

    // Keycloak keeps admin events under a realm attribute of their own, adminEventsExpiration, in seconds (#453).
    [Theory]
    [InlineData("src/AppHost/keycloak/realm.json")]
    [InlineData("src/AppHost/realms/realm.json")]
    public void Admin_events_expire_within_the_period_the_adr_records(string relativePath)
    {
        var realm = Read(relativePath);
        var expiration = realm["attributes"] is JsonObject attributes ? Value(attributes, "adminEventsExpiration") : "(missing)";

        long.TryParse(expiration, System.Globalization.CultureInfo.InvariantCulture, out var seconds).ShouldBeTrue(
            $"{relativePath} sets no attributes.adminEventsExpiration: Keycloak then keeps admin events forever, with the user representations they hold, past an erased account (#453, docs/adr/0040).");
        seconds.ShouldBeInRange(1, LongestEventsExpiration, $"{relativePath} keeps admin events for {seconds} s; docs/adr/0040 allows at most {LongestEventsExpiration} s (30 days).");
    }

    [Fact]
    public void Brute_force_detection_locks_out_after_a_few_failed_logins()
    {
        var realm = Published();

        Value(realm, "bruteForceProtected").ShouldBe("true", "The published realm must have bruteForceProtected true.");
        var failures = Value(realm, "failureFactor");
        int.TryParse(failures, System.Globalization.CultureInfo.InvariantCulture, out var factor).ShouldBeTrue($"The published realm must state its failureFactor, at most {MostFailures}; Keycloak's default is 30. It is {failures}.");
        factor.ShouldBeInRange(1, MostFailures, $"The published realm's failureFactor must be from 1 to {MostFailures}.");
    }

    // The port, STARTTLS and authentication come from Keycloak's environment, which the AppHost sets per deployment:
    // production's relay on 587 with STARTTLS and its credentials, staging's Mailpit inside the environment without either
    // (#130; pinned by AppHostStagingMailTests).
    [Fact]
    public void Mail_goes_through_the_deployments_smtp_server_as_keycloaks_environment_names_it()
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
                ["port"] = "${SMTP_PORT:587}",
                ["starttls"] = "${SMTP_STARTTLS:true}",
                ["ssl"] = "false",
                ["auth"] = "${SMTP_AUTH:true}",
            },
            "The published realm's smtpServer must name the deployment's SMTP server, its port, STARTTLS and authentication through Keycloak's environment, defaulting to the relay's 587 with STARTTLS and authentication when a variable is missing (#442), and never SSL on connect.");
    }

    // The staging end-to-end run's client (#130) is created on staging by the runbook alone, never imported: a realm that
    // declared it would give production a client that may manage every user.
    [Theory]
    [InlineData("src/AppHost/keycloak/realm.json")]
    [InlineData("src/AppHost/realms/realm.json")]
    public void Neither_realm_declares_the_staging_test_runner(string relativePath)
    {
        var clients = Read(relativePath)["clients"] as JsonArray ?? [];

        clients.OfType<JsonObject>().Select(client => (string?)client["clientId"]).ShouldNotContain("x1-runner", $"{relativePath} declares the client x1-runner.");
    }

    [Fact]
    public void Everything_else_equals_the_development_realm()
    {
        var published = WithoutTheDifferences(Published());
        var development = WithoutTheDifferences(Development());

        List<string> differences = [];
        Compare("$", development, published, differences);

        differences.ShouldBeEmpty(
            "src/AppHost/keycloak/realm.json differs from src/AppHost/realms/realm.json beyond the differences K1 and the security review allow (users, registrationAllowed, verifyEmail, sslRequired, bruteForceProtected, failureFactor, the event settings, smtpServer's server, port and security, pharma-training-web's redirectUris, webOrigins and directAccessGrantsEnabled, and the client admin-cli).");
    }

    private static JsonNode Published() => Read("src/AppHost/keycloak/realm.json");

    private static JsonNode Development() => Read("src/AppHost/realms/realm.json");

    private static JsonNode Read(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        File.Exists(path).ShouldBeTrue($"{relativePath} does not exist.");
        return JsonNode.Parse(File.ReadAllText(path)) ?? throw new ShouldAssertException($"{relativePath} is empty.");
    }

    /// <summary>A copy of a realm without the paths K1 and the security review let differ.</summary>
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

        if (copy["clients"] is JsonArray clients)
        {
            foreach (var publishedOnly in clients.OfType<JsonObject>().Where(candidate => PublishedClients.Contains((string?)candidate["clientId"])).ToList())
            {
                clients.Remove(publishedOnly);
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
