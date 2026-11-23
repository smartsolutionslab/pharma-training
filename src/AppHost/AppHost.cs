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
// Keycloak runs as a plain container. In run mode its administrator is "admin", with a password Aspire generates once and
// keeps in the AppHost's user secrets, and the realm in ./realms is imported on first start; published, the deployment
// sets the administrator and the mail server (Wiring/KeycloakSettings.cs), and it never runs a second replica.
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.6")
    .WithArgs("start-dev", "--import-realm")
    .WithAdministrator(secrets)
    .WithMailServer(mail, secrets)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithHttpEndpoint(port: 8080, targetPort: 8080)
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck("/health/ready", endpointName: "management")
    .WithSingleReplica();
if (builder.ExecutionContext.IsRunMode)
{
    keycloak.WithBindMount("./realms", "/opt/keycloak/data/import", isReadOnly: true);
    keycloak.WithVolume(VolumeNameGenerator.Generate(keycloak, "data"), "/opt/keycloak/data");
}
// Blob storage: one account, a container per module in the services' loop (docs/adr/0002). Each service signs short-lived
// upload and read URLs for its own container only: with Azurite's account key in run mode; published, with a user
// delegation key under its own identity, which may use its own container alone (Wiring/BlobContainerAccess.cs).
var storage = builder.AddBlobStorage();
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
    // The one entry from outside once published; the services, Keycloak and the broker stay inside the environment.
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(keycloak.GetEndpoint("http"))
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training"))
    // Published, the edge's image carries the web app's build and serves it; the browser reads its identity provider from
    // the edge's /config.json (Wiring/WebBuild.cs, docs/adr/0005).
    .PublishWithTheWebBuild();

// Links in mail lead into the web app: its dev server when run, its public address (the parameter frontend-url) deployed,
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
}
else
{
    frontendUrlParameter = builder.AddParameter("frontend-url");
    frontendUrl = ReferenceExpression.Create($"{frontendUrlParameter}");
}

// Published, the MigrationRunner's image, which every service runs as its init container; nothing in run mode.
var migrations = builder.AddMigrations();

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
        .WithConnectionStringsOnly(secrets)
        // The module's migrations, in its database alone, before the service starts: a project of its own in run mode,
        // the service's init container once published (Wiring/Migrations.cs).
        .RunsMigrationsFirst(module, database, migrations, secrets)
        // Its own database, as its module's role once published (Wiring/Databases.cs).
        .WithModuleDatabase(database, secrets)
        .WaitFor(database)
        .WithReference(keycloak.GetEndpoint("http"))
        .WaitFor(keycloak)
        .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training"))
        .WithMessaging(messaging, secrets, module)
        .WithBlobContainer(blobs, "blobs")
        .WithEnvironment("AzureOpenAI__Endpoint", azureOpenAIEndpoint)
        .WithEnvironment("AzureOpenAI__Deployment", azureOpenAIDeployment)
        .WithSecret("AzureOpenAI__ApiKey", azureOpenAIApiKey, secrets)
        .WithMail(mail, secrets)
        .WithEnvironment("Frontend__BaseUrl", frontendUrl);

    // The edge routes /<module>/... to the service it finds under the module's name, listed under Edge:Modules.
    host.WithReference(service)
        .WithEnvironment($"Edge__Modules__{module}", module)
        .WaitFor(service);
}

// Published, the browser uses the signed URLs from the edge's origin, which serves the web app, and from the web app's
// public address, frontend-url, alone (docs/adr/0002).
if (frontendUrlParameter is not null)
{
    storage.AllowBrowserAccessFromEdge(host, frontendUrlParameter);
}

builder.Build().Run();
