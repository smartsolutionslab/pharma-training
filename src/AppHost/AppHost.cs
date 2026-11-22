var builder = DistributedApplication.CreateBuilder(args);

// Published (aspire publish -e staging|production), everything runs in one Azure Container Apps environment and every
// secret is a parameter the deployment sets, kept in a Key Vault; run mode has neither (docs/adr/0004).
builder.AddContainerAppsEnvironment();
var secrets = DeploymentSecrets.For(builder);

// The server alone: every service gets a database of its own on it, in the AppHost's loop over the services.
var postgres = builder.AddDatabaseServer(secrets);
// Keycloak runs as a plain container. In run mode its administrator is "admin", with a password Aspire generates once and
// keeps in the AppHost's user secrets, and the realm in ./realms is imported on first start; published, the deployment
// sets the administrator and the mail server (Wiring/KeycloakSettings.cs), and it never runs a second replica.
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.6")
    .WithArgs("start-dev", "--import-realm")
    .WithAdministrator(secrets)
    .WithMailServer(secrets)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithHttpEndpoint(port: 8080, targetPort: 8080)
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck("/health/ready", endpointName: "management")
    .WithSingleReplica();

if (builder.ExecutionContext.IsRunMode)
{
    // Keycloak's email (verification, reset password) lands in Mailpit, whose inbox is http://localhost:8025; the realm points SMTP at it.
    var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.33")
        .WithHttpEndpoint(port: 8025, targetPort: 8025)
        .WithEndpoint(targetPort: 1025, name: "smtp")
        .WithHttpHealthCheck("/livez");
    keycloak.WaitFor(mailpit)
        .WithBindMount("./realms", "/opt/keycloak/data/import", isReadOnly: true);
    keycloak.WithVolume(VolumeNameGenerator.Generate(keycloak, "data"), "/opt/keycloak/data");
}
// Blob storage on Azurite: one account, a container per module in the services' loop (docs/adr/0002). Each service signs
// short-lived upload and read URLs for its own container only, with the account key.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume());
var messaging = builder.AddMessaging(secrets);
// Models deployed in your own Azure OpenAI resource in an EU region, through its OpenAI v1 endpoint; nothing is
// provisioned (docs/adr/0001). Set the endpoint (https://<resource>.openai.azure.com) and the key with dotnet user-secrets
// (Parameters:azure-openai-endpoint, Parameters:azure-openai-api-key) or the deployment's secret store. Both are read from
// configuration when they are needed and are empty when unset, so nothing waits for them: the application starts without a
// model, and a request that needs it answers 503 model-not-configured. Neither value is published to a manifest as a
// default. The deployment must name one in that resource (gpt-5 for harder questions).
var azureOpenAIEndpoint = builder.AddParameter("azure-openai-endpoint", () => builder.Configuration["Parameters:azure-openai-endpoint"] ?? string.Empty, publishValueAsDefault: false);
var azureOpenAIDeployment = builder.AddParameter("azure-openai-deployment", "gpt-5-mini", publishValueAsDefault: true);
// Published, the key is a secret the deployment must set, kept in the Key Vault (docs/adr/0005).
var azureOpenAIApiKey = secrets.IsPublishing
    ? secrets.Secret("azure-openai-api-key")
    : builder.AddParameter("azure-openai-api-key", () => builder.Configuration["Parameters:azure-openai-api-key"] ?? string.Empty, publishValueAsDefault: false, secret: true);

var host = builder.AddProject("host", "../Edge/Edge.csproj")
    // The one entry from outside once published; the services, Keycloak and the broker stay inside the environment.
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(keycloak.GetEndpoint("http"))
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training"));

foreach (var (module, project) in builder.ServiceHosts())
{
    // The service's own database on the one server, named after its module.
    var database = postgres.AddModuleDatabase(module);
    // The service's own blob container on the one storage account, named after its module; it reads it as the
    // connection string blobs and signs URLs for it alone.
    var blobs = storage.AddBlobContainer($"{module}-blobs", module);
    // The module's migrations, in its database alone, before the service starts.
    var migrations = builder.AddProject<Projects.MigrationRunner>($"{module}-migrations")
        .WithConnectionStringsOnly(secrets)
        .WithReference(database, "database")
        .WithEnvironment("Migrations__Module", module)
        .WaitFor(database);

    // Every service's launch profile names the same port; under the AppHost each gets one of its own.
    var service = builder.AddProject(module, project)
        .WithEndpoint("http", endpoint => endpoint.Port = null)
        .WithHttpHealthCheck("/health")
        .WithConnectionStringsOnly(secrets)
        .WaitForCompletion(migrations)
        .WithReference(database, "database")
        .WaitFor(database)
        .WithReference(keycloak.GetEndpoint("http"))
        .WaitFor(keycloak)
        .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training"))
        .WithMessaging(messaging, secrets)
        .WithEnvironment("Messaging__Queue", module)
        .WithReference(blobs, "blobs")
        .WithEnvironment("AzureOpenAI__Endpoint", azureOpenAIEndpoint)
        .WithEnvironment("AzureOpenAI__Deployment", azureOpenAIDeployment)
        .WithSecret("AzureOpenAI__ApiKey", azureOpenAIApiKey, secrets);

    // The edge routes /<module>/... to the service it finds under the module's name, listed under Edge:Modules.
    host.WithReference(service)
        .WithEnvironment($"Edge__Modules__{module}", module)
        .WaitFor(service);
}

// The web app's dev server, run mode only: it is not a Container App of its own once published (docs/adr/0004).
if (builder.ExecutionContext.IsRunMode)
{
    var web = builder.AddViteApp("web", "../../web/app")
        .WithPnpm()
        // OIDC redirect URIs name this port; the dev server proxies /api to the host.
        .WithEndpoint("http", endpoint => endpoint.Port = 5173)
        .WithReference(host)
        .WaitFor(host)
        .WaitFor(keycloak)
        .WithEnvironment("VITE_OIDC_AUTHORITY", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training"));
    // The browser PUTs uploads and GETs files with the signed URLs, straight to blob storage, from the web app's origin alone.
    storage.AllowBrowserAccessFrom(web);
}

builder.Build().Run();
