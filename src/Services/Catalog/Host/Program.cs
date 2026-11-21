using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var modules = ModuleDiscovery.Discover(AppContext.BaseDirectory);
foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
}

builder.Services.AddOpenApi(options => options.DescribeAsOpenApi31().DescribePaging());
builder.Services.AddKeycloakAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddKeycloakRoles();
builder.Services.AddUniqueConstraintResponses();
builder.Services.AddErrorResponses();
builder.Services.AddAzureOpenAIChatClient(builder.Configuration);
builder.Services.AddDispatcher();
builder.Services.AddDuplicateStreamResponses();
builder.Services.AddMessaging(builder.Configuration, modules);

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    // The frontend's client is generated from the document without signing in.
    app.MapOpenApi().AllowAnonymous();
}

app.MapDefaultEndpoints();

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();
