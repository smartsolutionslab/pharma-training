using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// An SMTP server on the loopback interface, without TLS or authentication, that refuses one command of every mail
/// with a permanent error (MAIL FROM, RCPT TO or DATA), so a real <c>SmtpMailSender</c> meets the server's own refusal
/// once its send has started. It counts the mails it took, which stays zero unless it was asked to refuse nothing.
/// </summary>
internal sealed class RefusingSmtpServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    private readonly CancellationTokenSource stopping = new();

    private readonly string refused;

    private readonly Task serving;

    private int taken;

    /// <param name="refused">The command to refuse: <c>MAIL</c>, <c>RCPT</c> or <c>DATA</c>.</param>
    public RefusingSmtpServer(string refused)
    {
        this.refused = refused;
        listener.Start();
        serving = ServeAsync(stopping.Token);
    }

    public string ConnectionString => $"smtp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>How many mails the server took, DATA and its end included.</summary>
    public int Taken => Volatile.Read(ref taken);

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        await serving.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        stopping.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await AnswerAsync(client, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task AnswerAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 test ESMTP");
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var verb = line.Split(' ', 2)[0].ToUpperInvariant();
            if (string.Equals(verb, refused, StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(verb == "DATA" ? "554 5.6.0 Message refused" : "550 5.1.1 Mailbox unavailable");
                continue;
            }

            switch (verb)
            {
                case "EHLO":
                case "HELO":
                    await writer.WriteLineAsync("250 test");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 Go ahead");
                    var lines = 0;
                    while (await reader.ReadLineAsync(cancellationToken) is { } data && data != ".")
                    {
                        lines++;
                    }

                    if (lines > 0)
                    {
                        Interlocked.Increment(ref taken);
                    }

                    await writer.WriteLineAsync("250 OK");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 OK");
                    break;
            }
        }
    }
}
