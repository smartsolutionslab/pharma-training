var builder = DistributedApplication.CreateBuilder(args);

// The server alone: every service gets a database of its own on it, in the AppHost's loop over the services.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();
// Keycloak runs as a plain container, development only (see docs/adr). Its administrator is "admin", with a password
// Aspire generates once and keeps in the AppHost's user secrets; the realm in ./realms is imported on first start.
var keycloakPassword = builder.AddResource(ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, "keycloak-password"));
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.6")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakPassword)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithHttpEndpoint(port: 8080, targetPort: 8080)
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck("/health/ready", endpointName: "management")
    .WithBindMount("./realms", "/opt/keycloak/data/import", isReadOnly: true);
keycloak.WithVolume(VolumeNameGenerator.Generate(keycloak, "data"), "/opt/keycloak/data");

if (builder.ExecutionContext.IsRunMode)
{
    // Keycloak's email (verification, reset password) lands in Mailpit, whose inbox is http://localhost:8025; the realm points SMTP at it.
    var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.33")
        .WithHttpEndpoint(port: 8025, targetPort: 8025)
        .WithEndpoint(targetPort: 1025, name: "smtp")
        .WithHttpHealthCheck("/livez");
    keycloak.WaitFor(mailpit);
}
// Blob storage on Azurite: one account, a container per module in the services' loop (docs/adr/0002). Each service signs
// short-lived upload and read URLs for its own container only, with the account key.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume());
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithDataVolume();
// Models deployed in your own Azure OpenAI resource in an EU region, through its OpenAI v1 endpoint; nothing is
// provisioned (docs/adr/0001). Set the endpoint (https://<resource>.openai.azure.com) and the key with dotnet user-secrets
// (Parameters:azure-openai-endpoint, Parameters:azure-openai-api-key) or the deployment's secret store. Both are read from
// configuration when they are needed and are empty when unset, so nothing waits for them: the application starts without a
// model, and a request that needs it answers 503 model-not-configured. Neither value is published to a manifest as a
// default. The deployment must name one in that resource (gpt-5 for harder questions).
var azureOpenAIEndpoint = builder.AddParameter("azure-openai-endpoint", () => builder.Configuration["Parameters:azure-openai-endpoint"] ?? string.Empty, publishValueAsDefault: false);
var azureOpenAIDeployment = builder.AddParameter("azure-openai-deployment", "gpt-5-mini", publishValueAsDefault: true);
var azureOpenAIApiKey = builder.AddParameter("azure-openai-api-key", () => builder.Configuration["Parameters:azure-openai-api-key"] ?? string.Empty, publishValueAsDefault: false, secret: true);

var host = builder.AddProject<Projects.Edge>("host")
    .WithHttpHealthCheck("/health")
    .WithReference(keycloak.GetEndpoint("http"))
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training"));

foreach (var (module, project) in builder.ServiceHosts())
{
    // The service's own database on the one server, named after its module.
    var database = postgres.AddDatabase($"{module}-database", module);
    // The service's own blob container on the one storage account, named after its module; it reads it as the
    // connection string blobs and signs URLs for it alone.
    var blobs = storage.AddBlobContainer($"{module}-blobs", module);
    // The module's migrations, in its database alone, before the service starts.
    var migrations = builder.AddProject<Projects.MigrationRunner>($"{module}-migrations")
        .WithReference(database, "database")
        .WithEnvironment("Migrations__Module", module)
        .WaitFor(database);

    // Every service's launch profile names the same port; under the AppHost each gets one of its own.
    var service = builder.AddServiceHost(module, project)
        .WithEndpoint("http", endpoint => endpoint.Port = null)
        .WithHttpHealthCheck("/health")
        .WaitForCompletion(migrations)
        .WithReference(database, "database")
        .WaitFor(database)
        .WithReference(keycloak.GetEndpoint("http"))
        .WaitFor(keycloak)
        .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training"))
        .WithReference(messaging)
        .WaitFor(messaging)
        .WithEnvironment("Messaging__Queue", module)
        .WithReference(blobs, "blobs")
        .WithEnvironment("AzureOpenAI__Endpoint", azureOpenAIEndpoint)
        .WithEnvironment("AzureOpenAI__Deployment", azureOpenAIDeployment)
        .WithEnvironment("AzureOpenAI__ApiKey", azureOpenAIApiKey);

    // The edge routes /<module>/... to the service it finds under the module's name, listed under Edge:Modules.
    host.WithReference(service)
        .WithEnvironment($"Edge__Modules__{module}", module)
        .WaitFor(service);
}

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

builder.Build().Run();
