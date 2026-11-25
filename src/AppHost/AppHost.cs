var builder = DistributedApplication.CreateBuilder(args);

// Published (aspire publish -e staging|production), everything runs in one Azure Container Apps environment and every
// secret is a parameter the deployment sets, kept in a Key Vault; run mode has neither (docs/adr/0005).
builder.AddContainerAppsEnvironment();
var secrets = DeploymentSecrets.For(builder);
// Published, each app reads the Key Vault secrets it references alone, computed once the model is final, whatever is added
// after this line (docs/adr/0005).
await secrets.GrantEachAppTheSecretsItReadsAsync(builder);

// The server alone: every service gets a database of its own on it, in the AppHost's loop over the services.
var postgres = builder.AddDatabaseServer(secrets);
// The one SMTP server Keycloak and the services send through: Mailpit in run mode, whose inbox is http://localhost:8025;
// published, the server the deployment names in the smtp-* parameters (Wiring/MailServer.cs, docs/adr/0004 and 0005).
var mail = MailServer.Add(builder, secrets);
// Published, the MigrationRunner's image, which every service and Keycloak run as their init container; nothing in run mode.
var migrations = builder.AddMigrations();
// Keycloak runs as a plain container. In run mode its administrator is "admin", with a password Aspire generates once and
// keeps in the AppHost's user secrets, the realm in ./realms is imported on first start in start-dev, and its data stays on
// a volume. Published, the deployment sets the administrator and the mail server (Wiring/KeycloakSettings.cs); it runs an
// image built with the published realm (./keycloak), imported once, and keeps its data in its own database as its own
// role, keycloak_app, which its init container ensures (Wiring/KeycloakPublishing.cs); it never runs a second replica.
var keycloak = builder.AddContainer("keycloak", KeycloakPublishing.Image, KeycloakPublishing.Tag)
    .WithAdministrator(secrets)
    .WithMailServer(mail, secrets)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithHttpEndpoint(port: 8080, targetPort: 8080)
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck("/health/ready", endpointName: "management")
    .WithSingleReplica()
    .WithRealmAndStore(postgres, migrations, secrets);
// Published, the browser reaches Keycloak through the auth proxy alone, at https://auth.<the environment's default domain>,
// which forwards the realm pharma-training and the static resources (Wiring/AuthProxy.cs); in run mode, Keycloak's own
// endpoint. That address is the issuer the edge and every service validate.
var auth = builder.AddAuthProxy(keycloak);
var authority = AuthProxy.Authority(keycloak, auth);
// Blob storage: one account, a container per module in the services' loop (docs/adr/0002). Each service signs short-lived
// upload and read URLs for its own container only: with Azurite's account key in run mode; published, with a user
// delegation key under its own identity, which may use its own container alone (Wiring/BlobContainerAccess.cs).
var storage = builder.AddBlobStorage();
// Published, the environment runs in a virtual network, and the server and the Key Vault are reached through private
// endpoints in it alone, with no public endpoint of their own; the services reach the blobs through one too, while the
// account's public endpoint stays for the browsers' signed URLs (Wiring/PrivateNetwork.cs, docs/adr/0038).
builder.AddPrivateNetwork(secrets, postgres, storage);
// The broker, with a user of its own for every service, who may use its own queue, publish its own module's events and
// bind to the events it consumes alone (Wiring/Messaging.cs, docs/adr/0011).
var messaging = builder.AddMessaging(secrets);
// Models deployed in your own Azure OpenAI resource in an EU region, through its OpenAI v1 endpoint; nothing is
// provisioned (docs/adr/0001). Set the endpoint (https://<resource>.openai.azure.com) and the key with dotnet user-secrets
// (Parameters:azure-openai-endpoint, Parameters:azure-openai-api-key) or the deployment's secret store. Both are read from
// configuration when they are needed and are empty when unset, so nothing waits for them: the application starts without a
// model, and a request that needs it answers 503 model-not-configured. Neither value is published to a manifest as a
// default. The deployment must name one in that resource (gpt-5 for harder questions).
var azureOpenAIEndpoint = builder.AddParameter("azure-openai-endpoint", () => builder.Configuration["Parameters:azure-openai-endpoint"] ?? string.Empty, publishValueAsDefault: false);
var azureOpenAIDeployment = builder.AddParameter("azure-openai-deployment", "gpt-5-mini", publishValueAsDefault: true);
// Published, the key is a secret the deployment must set, kept in the Key Vault (docs/adr/0006).
var azureOpenAIApiKey = secrets.IsPublishing
    ? secrets.Secret("azure-openai-api-key")
    : builder.AddParameter("azure-openai-api-key", () => builder.Configuration["Parameters:azure-openai-api-key"] ?? string.Empty, publishValueAsDefault: false, secret: true);

var host = builder.AddProject<Projects.Edge>("host")
    // With the auth proxy, the one entry from outside once published; the services, Keycloak and the broker stay inside.
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(keycloak.GetEndpoint("http"))
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", authority)
    // Published, the client's address is the one the ingress appended to X-Forwarded-For (Wiring/IngressForwarding.cs).
    .TrustsTheIngressHop()
    // Published, the edge's image carries the web app's build and serves it; the browser reads its identity provider from
    // the edge's /config.json (Wiring/WebBuild.cs, docs/adr/0005).
    .PublishWithTheWebBuild();
// Published, the realm's web client accepts the edge's origin, which serves the web app.
keycloak.WithWebOrigin(host);

// Links in mail lead into the web app, for the services that send mail: its dev server when run, its public address (the parameter frontend-url) deployed,
// which no endpoint reference knows.
ReferenceExpression frontendUrl;
IResourceBuilder<ParameterResource>? frontendUrlParameter = null;
if (builder.ExecutionContext.IsRunMode)
{
    // The web app's dev server, run mode only: published, the edge serves its build (docs/adr/0005). It reads its identity
    // provider from the edge's /config.json, through the dev server's proxy.
    var web = builder.AddViteApp("web", "../../web/app")
        .WithPnpm()
        // OIDC redirect URIs name this port; the dev server proxies /api to the host.
        .WithEndpoint("http", endpoint => endpoint.Port = 5173)
        .WithReference(host)
        .WaitFor(host)
        .WaitFor(keycloak);
    // The browser PUTs uploads and GETs files with the signed URLs, straight to blob storage, from the web app's origin alone.
    storage.AllowBrowserAccessFrom(web);
    frontendUrl = ReferenceExpression.Create($"{web.GetEndpoint("http")}");
    // Demo data, seeded through the edge once it, Keycloak and the web app are up; off with DemoSeeder:Enabled=false
    // (Wiring/DemoSeeding.cs, docs/adr/0041).
    builder.AddDemoSeeder(host, web, keycloak);
}
else
{
    // The storage account's CORS rule allows it and mail links lead to it: an https origin, which publishing checks.
    frontendUrlParameter = builder.AddParameter("frontend-url").RequireHttpsOrigin();
    frontendUrl = ReferenceExpression.Create($"{frontendUrlParameter}");
}

foreach (var (module, project) in builder.ServiceHosts())
{
    // The service's own database on the one server, named after its module.
    var database = postgres.AddModuleDatabase(module);
    // The service's own blob container on the one storage account, named after its module; it reads it as the
    // connection string blobs and signs URLs for it alone.
    var blobs = storage.AddBlobContainer($"{module}-blobs", module);
    // Every service's launch profile names the same port; under the AppHost each gets one of its own.
    var service = builder.AddServiceHost(module, project)
        .WithEndpoint("http", endpoint => endpoint.Port = null)
        .WithHttpHealthCheck("/health")
        // Published, its internal ingress's X-Forwarded-For hop is taken off, leaving the client's the edge sent.
        .TrustsTheIngressHop()
        .WithConnectionStringsOnly(secrets)
        // The module's migrations, in its database alone, before the service starts: a project of its own in run mode,
        // the service's init container once published (Wiring/Migrations.cs).
        .RunsMigrationsFirst(module, database, migrations, secrets)
        // Its own database, as its module's role once published (Wiring/Databases.cs).
        .WithModuleDatabase(database, secrets)
        .WaitFor(database)
        .WithReference(keycloak.GetEndpoint("http"))
        .WaitFor(keycloak)
        .WithEnvironment("Authentication__Authority", authority)
        .WithMessaging(messaging, secrets, module)
        .WithBlobContainer(blobs, "blobs")
        .WithEnvironment("AzureOpenAI__Endpoint", azureOpenAIEndpoint)
        .WithEnvironment("AzureOpenAI__Deployment", azureOpenAIDeployment)
        .WithSecret("AzureOpenAI__ApiKey", azureOpenAIApiKey, secrets);

    // Only a service whose project says <SendsMail>true</SendsMail> gets the mail server and the web app's address its
    // links lead to; published, the others never reference the mail secret, so they are not granted it (docs/adr/0004).
    if (ServiceHostProjects.SendsMail(project))
    {
        service.WithMail(mail, secrets)
            .WithEnvironment("Frontend__BaseUrl", frontendUrl);
    }
    else if (ServiceHostProjects.LinksToFrontend(project))
    {
        // A service that links into the web app without mailing, as its project says with <LinksToFrontend>true</LinksToFrontend>,
        // gets the web app's address alone: such a host prints links into the web app on documents it renders.
        service.WithEnvironment("Frontend__BaseUrl", frontendUrl);
    }

    // The edge routes /<module>/... to the service it finds under the module's name, listed under Edge:Modules.
    host.WithReference(service)
        .WithEnvironment($"Edge__Modules__{module}", module)
        .WaitFor(service);
}

// Published, the browser uses the signed URLs from the edge's origin, which serves the web app, and from the web app's
// public address, frontend-url, alone (docs/adr/0002); index.html's Content-Security-Policy lets the page call blob
// storage's origin, Edge:BlobEndpoint, and none other but the edge's and the identity provider's (docs/adr/0005).
if (frontendUrlParameter is not null)
{
    storage.AllowBrowserAccessFromEdge(host, frontendUrlParameter);
    host.WithEnvironment("Edge__BlobEndpoint", storage.Resource.BlobEndpoint);
}

builder.Build().Run();
