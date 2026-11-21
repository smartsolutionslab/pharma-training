using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>The SMTP server in <c>ConnectionStrings:mail</c>. Stub for #9's red tests (T6).</summary>
internal sealed class SmtpSettings
{
    private SmtpSettings()
    {
    }

    public string Host { get; private init; } = string.Empty;

    public int Port { get; private init; }

    public string? User { get; private init; }

    public string? Password { get; private init; }

    public SecureSocketOptions Security { get; private init; }

    public static SmtpSettings From(IConfiguration configuration) => throw new NotImplementedException();
}
