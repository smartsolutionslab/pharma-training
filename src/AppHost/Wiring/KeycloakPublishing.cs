using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Dotnet;

namespace Aspire.Hosting;

/// <summary>
/// Where Keycloak's realm and data come from (docs/adr/0005). In run mode it is Keycloak's own image in <c>start-dev</c>,
/// importing the development realm from <c>./realms</c> on its first start and keeping its data on a volume, as it always
/// was. Published, it runs an image built from Keycloak's own, optimized for PostgreSQL with health checks and holding the
/// published realm (<c>./keycloak/realm.json</c>), which it imports once, on its first start (B10 on #114); it keeps its data
/// in its own database on the Flexible Server, in the schema <c>keycloak</c>, logged in over TLS as <c>keycloak_app</c>
/// with the password from the vault, a role its init container ensures (<see cref="Migrations.EnsuresOwnedSchemaFirst"/>,
/// Q2 on #114). It serves plain HTTP behind the auth proxy (<see cref="AuthProxy"/>) and the Container Apps ingress, whose
/// <c>X-Forwarded-*</c> headers it reads; its public address and the web app's origin come from the environment's default
/// domain (<see cref="PublicAddresses"/>).
/// </summary>
public static class KeycloakPublishing
{
    /// <summary>The image both modes start from, and the published build's base.</summary>
    public const string Image = "quay.io/keycloak/keycloak";

    public const string Tag = "26.6";

    /// <summary>
    /// The published build's base, by tag and digest (the security review of #114): a tag can be moved to an image nobody
    /// reviewed. Run mode keeps the tag alone. Dependabot cannot see an image named in C#, so a new Keycloak is pinned here by
    /// hand, with <see cref="Tag"/>, from <c>docker buildx imagetools inspect quay.io/keycloak/keycloak:&lt;tag&gt;</c>.
    /// </summary>
    public const string PinnedImage = $"{Image}:{Tag}@sha256:0aae0de7fca85525f727d3354df17896092de8bb26ae4c12d89c77e5df8cbce4";

    // The schema Keycloak's tables live in (X3), owned by its role.
    private const string Schema = "keycloak";

    private const string RealmImport = "/opt/keycloak/data/import";

    // Where kcadm.sh is, and kcadm-otp.sh beside it: the administrator's sign-in with a one-time code (#278).
    private const string AdminTools = "/opt/keycloak/bin";

    /// <summary>
    /// In run mode, <c>start-dev</c> with the development realm and a data volume; published, the built image, started
    /// optimized, logging in to <paramref name="server"/>'s Keycloak database as its own role.
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithRealmAndStore(
        this IResourceBuilder<ContainerResource> keycloak,
        IResourceBuilder<IResourceWithConnectionString> server,
        IResourceBuilder<DotnetProjectResource>? migrations,
        DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(keycloak);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!keycloak.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return keycloak
                .WithArgs("start-dev", "--import-realm")
                .WithBindMount("./realms", RealmImport, isReadOnly: true)
                .WithVolume(VolumeNameGenerator.Generate(keycloak, "data"), "/opt/keycloak/data");
        }

        if (server.Resource is not AzurePostgresFlexibleServerResource flexibleServer)
        {
            throw new InvalidOperationException($"Published, Keycloak keeps its data on the Flexible Server; {server.Resource.Name} is none.");
        }

        var database = Databases.KeycloakDatabaseOf(server);
        var databaseName = ((AzurePostgresFlexibleServerDatabaseResource)database.Resource).DatabaseName;
        return keycloak
            .WithDockerfileFactory("./keycloak", _ => Dockerfile())
            .WithArgs("start", "--optimized", "--import-realm")
            .WithEnvironment("KC_DB_URL_HOST", new BicepOutputReference("hostName", flexibleServer))
            .WithEnvironment("KC_DB_URL_DATABASE", databaseName)
            .WithEnvironment("KC_DB_URL_PROPERTIES", "?sslmode=require")
            .WithEnvironment("KC_DB_SCHEMA", Schema)
            .WithEnvironment("KC_DB_USERNAME", Databases.RoleName(databaseName))
            .WithEnvironment("KC_DB_PASSWORD", Databases.RolePassword(database, secrets))
            .WithEnvironment("KC_HTTP_ENABLED", "true")
            .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
            .EnsuresOwnedSchemaFirst(Schema, database, migrations, secrets);
    }

    /// <summary>
    /// Published, the origin the realm's web client accepts, <c>${WEB_ORIGIN}</c>: the edge's public address, which serves
    /// the web app. Not <c>frontend-url</c>, which the deployment can set only once the environment exists, after the realm
    /// was imported once. In run mode nothing: the development realm names the web app's dev server.
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithWebOrigin(this IResourceBuilder<ContainerResource> keycloak, IResourceBuilder<DotnetProjectResource> edge)
    {
        ArgumentNullException.ThrowIfNull(keycloak);
        ArgumentNullException.ThrowIfNull(edge);

        return keycloak.ApplicationBuilder.ExecutionContext.IsPublishMode
            ? keycloak.WithEnvironment("WEB_ORIGIN", PublicAddresses.Of(edge))
            : keycloak;
    }

    /// <summary>
    /// The published image: built for PostgreSQL with health checks in one stage, run from another with the published realm
    /// in Keycloak's import directory, and <c>kcadm-otp.sh</c> beside kcadm.sh, which signs the administrator in with a
    /// one-time code kcadm.sh cannot send (#278), executable whatever mode the checkout gave it (#331).
    /// </summary>
    private static string Dockerfile() =>
        $"""
        FROM {PinnedImage} AS builder
        RUN /opt/keycloak/bin/kc.sh build --db=postgres --health-enabled=true

        FROM {PinnedImage}
        COPY --from=builder /opt/keycloak/ /opt/keycloak/
        COPY realm.json {RealmImport}/realm.json
        COPY KcadmOtp.java {AdminTools}/
        COPY --chmod=0755 kcadm-otp.sh {AdminTools}/

        """;
}
