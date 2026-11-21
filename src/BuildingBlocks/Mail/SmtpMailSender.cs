using Microsoft.Extensions.Configuration;
using MimeKit;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>Sends mail over SMTP with MailKit. Stub for #9's red tests (T6).</summary>
public sealed class SmtpMailSender : IMailSender
{
    public SmtpMailSender(IConfiguration configuration) => Ensure.That(configuration).IsNotNull();

    public Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken) => throw new NotImplementedException();

    internal static MimeMessage Compose(OutgoingMail mail, MailboxAddress from) => throw new NotImplementedException();
}
