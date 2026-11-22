using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// Outside Development, refuses to let a host start without usable mail settings, so a deployment that would answer
/// invitations and mail nothing never goes healthy. It checks each setting with the parser the send and the link use,
/// so the check and the send cannot disagree, and names each bad one with its AppHost parameter, never its value. In
/// Development it reads nothing: <c>aspire run</c> and the tests supply the settings, or a developer mails nothing.
/// </summary>
internal sealed class MailSettingsValidation(IHostEnvironment environment, IConfiguration configuration) : IValidateOptions<MailStartup>
{
    public ValidateOptionsResult Validate(string? name, MailStartup options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        Check(() => SmtpSettings.From(configuration), "ConnectionStrings:mail (the AppHost's connection string mail) is missing or unusable.", failures);
        Check(() => SmtpMailSender.From(configuration), "Mail:From (mail-from) is missing or no e-mail address.", failures);
        Check(() => FrontendLinks.BaseUrl(configuration), "Frontend:BaseUrl (frontend-url) is missing, not absolute, or http to a host that is not loopback.", failures);

        if (failures.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(failures);
    }

    private static void Check(Func<object> read, string failure, List<string> failures)
    {
        try
        {
            read();
        }
        catch (InvalidOperationException)
        {
            // The parsers' own messages never carry a value either; this one also names the AppHost parameter.
            failures.Add(failure);
        }
    }
}
