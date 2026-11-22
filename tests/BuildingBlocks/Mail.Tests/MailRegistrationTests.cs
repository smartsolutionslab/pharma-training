using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// Every host gets one mail sender and one link builder; neither reads its settings before it is used. Outside
/// Development a host without valid mail settings does not start, naming each one that is missing or wrong.
/// </summary>
public class MailRegistrationTests
{
    private const string Password = "secret";

    [Fact]
    public void Registering_twice_registers_one_sender_and_one_link_builder_and_reads_no_configuration()
    {
        var configuration = new UnreadableConfiguration();
        var services = new ServiceCollection();

        services.AddModuleMail(configuration);
        services.AddModuleMail(configuration);

        services.Count(service => service.ServiceType == typeof(IMailSender)).ShouldBe(1);
        services.Count(service => service.ServiceType == typeof(IFrontendLinks)).ShouldBe(1);
        services.Where(service =>
                service.ServiceType.IsGenericType
                && service.ServiceType.GetGenericTypeDefinition() == typeof(IValidateOptions<>)
                && service.ServiceType.GetGenericArguments()[0].Assembly == typeof(IMailSender).Assembly)
            .ShouldHaveSingleItem();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMailSender>().ShouldBeOfType<SmtpMailSender>();
        provider.GetRequiredService<IFrontendLinks>().ShouldBeOfType<FrontendLinks>();
    }

    [Fact]
    public async Task Outside_development_a_host_without_the_mail_settings_fails_to_start_naming_each()
    {
        using var host = Host(Environments.Production);

        var refused = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        refused.Message.ShouldContain("ConnectionStrings:mail", Case.Sensitive);
        refused.Message.ShouldContain("Mail:From", Case.Sensitive);
        refused.Message.ShouldContain("Frontend:BaseUrl", Case.Sensitive);
    }

    [Fact]
    public async Task Outside_development_a_frontend_over_http_to_another_host_fails_to_start()
    {
        using var host = Host(
            Environments.Production,
            ("ConnectionStrings:mail", $"smtp://user:{Password}@mail.example:587"),
            ("Mail:From", "noreply@example.com"),
            ("Frontend:BaseUrl", "http://app.example"));

        var refused = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        refused.Message.ShouldContain("Frontend:BaseUrl", Case.Sensitive);
        refused.Message.ShouldNotContain(Password, Case.Sensitive);
        refused.Message.ShouldNotContain("http://app.example", Case.Sensitive);
    }

    [Fact]
    public async Task Outside_development_a_host_with_its_mail_settings_starts()
    {
        using var host = Host(
            Environments.Production,
            ("ConnectionStrings:mail", $"smtp://user:{Password}@mail.example:587"),
            ("Mail:From", "noreply@example.com"),
            ("Frontend:BaseUrl", "https://app.example"));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task In_development_a_host_without_them_starts()
    {
        using var host = Host(Environments.Development);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A bare host in the environment with the settings given, registering the mail twice as two modules would.</summary>
    private static IHost Host(string environment, params (string Key, string Value)[] settings)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment, DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));
        builder.Services.AddModuleMail(builder.Configuration);
        builder.Services.AddModuleMail(builder.Configuration);
        return builder.Build();
    }
}
