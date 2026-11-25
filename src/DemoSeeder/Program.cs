using Microsoft.Extensions.Configuration;
using SmartSolutionsLab.PharmaTraining.DemoSeeder;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();
using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
return await SeederRun.RunAsync(configuration, SeedPlan.Demo, handler, TimeProvider.System, Console.Out, CancellationToken.None);
