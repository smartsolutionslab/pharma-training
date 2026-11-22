using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// Every way the transport fails against a real socket surfaces as <see cref="MailNotSentException"/> naming only the
/// kind of failure: never the server's text, its address or port, or the recipient.
/// </summary>
public class SmtpMailSenderTransportFailureTests
{
    private const string ServerKey = "ConnectionStrings:mail";

    private const string FromKey = "Mail:From";

    private const string Recipient = "anna@example.org";

    /// <summary>Text only the server writes, so a message that holds it carries what the server said.</summary>
    private const string ServerText = "only-the-server-wrote-this";

    private static readonly OutgoingMail Invitation = new(Recipient, "Einladung", "Text");

    [Fact]
    public async Task A_server_that_refuses_the_connection_fails_as_not_sent_without_its_address_or_port()
    {
        // ConnectAsync throws the SocketException unwrapped; it is no IOException.
        var port = ClosedPort();
        var sender = new SmtpMailSender(Settings.Of((ServerKey, $"smtp://127.0.0.1:{port}"), (FromKey, "noreply@example.com")));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.Message.ShouldNotContain("127.0.0.1", Case.Insensitive);
        failed.Message.ShouldNotContain(port.ToString(CultureInfo.InvariantCulture), Case.Insensitive);
        failed.Message.ShouldNotContain(Recipient, Case.Insensitive);
        failed.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task A_server_that_wants_a_login_first_fails_as_not_sent_without_the_servers_text_or_the_address()
    {
        // 530 to MAIL FROM: MailKit throws ServiceNotAuthenticatedException with the server's text as its message.
        await using var server = new SmtpTestServer(refuse: ("MAIL", "530 5.7.0 " + ServerText + " <{0}>"));
        var sender = new SmtpMailSender(Settings.Of((ServerKey, server.ConnectionString), (FromKey, "noreply@example.com")));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    /// <summary>A port nothing listens on: bound by a listener of the test's, read, and freed again.</summary>
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void ShouldNameNeitherTheServersTextNorTheRecipient(MailNotSentException failed)
    {
        failed.Message.ShouldNotContain(ServerText, Case.Insensitive);
        failed.Message.ShouldNotContain(Recipient, Case.Insensitive);
        failed.InnerException.ShouldBeNull();
    }
}
