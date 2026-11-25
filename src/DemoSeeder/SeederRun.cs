using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// One run of the seeder: reads and guards the <c>DemoSeeder:*</c> settings, then walks the plan through the edge, Keycloak
/// and Mailpit, all through the one handler. Answers the exit code: 0 seeded (or nothing to seed), 1 a step failed, 2 the
/// settings were refused before any call.
/// </summary>
public static class SeederRun
{
    public const int Seeded = 0;

    public const int StepFailed = 1;

    public const int SettingsRefused = 2;

    public static async Task<int> RunAsync(
        IConfiguration configuration,
        SeedPlan plan,
        HttpMessageHandler handler,
        TimeProvider time,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(plan).IsNotNull();
        Ensure.That(output).IsNotNull();

        if (!SeederSettings.TryRead(configuration, out var settings, out var refusal))
        {
            output.WriteLine(refusal);
            return SettingsRefused;
        }

        // The handler follows no redirect and keeps no cookie; the client sends every request once (POSTs are never retried).
        using var http = new HttpClient(handler, disposeHandler: false);
        try
        {
            var created = await Seeder.SeedAsync(plan, settings, http, time, output, cancellationToken);
            output.WriteLine($"Demo data: {created} created");
            return Seeded;
        }
        catch (SeedingException failure)
        {
            output.WriteLine($"Demo data: a step failed: {failure.Message}");
        }
        catch (HttpRequestException failure)
        {
            output.WriteLine($"Demo data: a step failed: a request did not reach its server ({failure.HttpRequestError}).");
        }
        catch (JsonException)
        {
            output.WriteLine("Demo data: a step failed: an answer was not the JSON it should be.");
        }

        return StepFailed;
    }
}
