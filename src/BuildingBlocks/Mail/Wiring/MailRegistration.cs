using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

namespace Microsoft.Extensions.DependencyInjection;

public static class MailRegistration
{
    /// <summary>
    /// Registers <see cref="IMailSender"/> (SMTP, the connection string <c>mail</c>, from <c>Mail:From</c>) and
    /// <see cref="IFrontendLinks"/> (<c>Frontend:BaseUrl</c>) once per host (docs/adr/0004). Registering reads nothing.
    /// Outside Development the host checks the three settings at start-up and does not start without them; in
    /// Development it starts, and a missing setting fails the first mail or link, naming it.
    /// </summary>
    public static IServiceCollection AddModuleMail(this IServiceCollection services, IConfiguration configuration)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(configuration).IsNotNull();

        services.TryAddSingleton<IMailSender>(provider => new SmtpMailSender(configuration, provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddSingleton<IFrontendLinks>(_ => new FrontendLinks(configuration));

        // Outside Development a host without usable mail settings does not start (MailSettingsValidation). Both calls
        // are idempotent, so one check runs however many modules the host registers.
        services.AddOptions<MailStartup>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MailStartup>, MailSettingsValidation>(
            provider => new MailSettingsValidation(provider.GetRequiredService<IHostEnvironment>(), configuration)));
        return services;
    }
}
