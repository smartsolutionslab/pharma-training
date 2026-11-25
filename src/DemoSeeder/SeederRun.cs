using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// One run of the seeder: reads and guards the <c>DemoSeeder:*</c> settings, then walks the plan through the edge, Keycloak
/// and Mailpit, all through the one handler. Answers the exit code: 0 seeded (or nothing to seed), 1 a step failed, 2 the
/// settings were refused before any call.
/// </summary>
public static class SeederRun
{
    // stub for #457
    public static Task<int> RunAsync(
        IConfiguration configuration,
        SeedPlan plan,
        HttpMessageHandler handler,
        TimeProvider time,
        TextWriter output,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException("stub for #457");
}
