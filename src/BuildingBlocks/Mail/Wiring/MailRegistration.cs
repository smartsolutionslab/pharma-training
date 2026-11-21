using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

namespace Microsoft.Extensions.DependencyInjection;

public static class MailRegistration
{
    /// <summary>
    /// Registers <see cref="IMailSender"/> (SMTP, the connection string <c>mail</c>, from <c>Mail:From</c>) and
    /// <see cref="IFrontendLinks"/> (<c>Frontend:BaseUrl</c>) once per host (docs/adr/0004). Registering reads nothing:
    /// a host without the settings starts, and fails only when it first mails or links, naming what is missing.
    /// </summary>
    public static IServiceCollection AddModuleMail(this IServiceCollection services, IConfiguration configuration)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(configuration).IsNotNull();

        services.TryAddSingleton<IMailSender>(_ => new SmtpMailSender(configuration));
        services.TryAddSingleton<IFrontendLinks>(_ => new FrontendLinks(configuration));
        return services;
    }
}
