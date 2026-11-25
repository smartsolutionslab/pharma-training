var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
// The edge's spans keep no path beyond the module, which may carry a credential (#311).
builder.Services.AddPathRedactedTelemetry();
builder.Services.AddModuleRoutes(builder.Configuration);

builder.Services.AddOpenApi(options => options.DescribeAsOpenApi31().DescribePaging());
builder.Services.AddKeycloakAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddKeycloakRoles();

var app = builder.Build();

// What every answer tells the browser: nosniff, a strict Referrer-Policy and, deployed, HTTPS alone.
app.UseSecurityHeaders();
// The web app calls the edge under /api; published, the edge serves the app's build, page loads before the modules' routes.
app.UseApiPrefix();
app.UseSpaHosting();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    // The frontend's client is generated from the document without signing in.
    app.MapOpenApi().AllowAnonymous();
}
app.UseServiceDocuments();

app.MapDefaultEndpoints();
app.MapFrontendConfiguration();
app.MapGet("/me", (System.Security.Claims.ClaimsPrincipal user) => TypedResults.Ok(new SmartSolutionsLab.PharmaTraining.Host.CurrentUser(user.FindFirst("preferred_username")?.Value)))
    .RequireAuthorization();
app.MapReverseProxy();

app.Run();
