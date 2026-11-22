using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// An SMTP server on the loopback interface, without TLS or authentication, that records the recipients and messages it
/// is given. It can hold its answer to any of the commands named when it is created until the test releases it, so a
/// test decides how long each step of a send takes. It can instead refuse one command with an answer of the test's, in
/// which <c>{0}</c> is the address the command carries, or reset the connection when one command arrives.
/// </summary>
internal sealed class SmtpTestServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    private readonly CancellationTokenSource stopping = new();

    private readonly Dictionary<string, Hold> holds;

    private readonly ConcurrentQueue<string> recipients = new();

    private readonly ConcurrentQueue<string> messages = new();

    private readonly (string Verb, string Answer)? refusal;

    private readonly string? resetAt;

    private readonly Task serving;

    public SmtpTestServer(params string[] held)
        : this(null, null, held)
    {
    }

    /// <param name="refuse">The command to refuse, such as <c>RCPT</c>, and the answer, such as <c>550 5.1.1 &lt;{0}&gt;: Recipient address rejected</c>.</param>
    public SmtpTestServer((string Verb, string Answer) refuse)
        : this(refuse, null, [])
    {
    }

    private SmtpTestServer((string Verb, string Answer)? refuse, string? reset, string[] held)
    {
        refusal = refuse;
        resetAt = reset;
        holds = held.ToDictionary(verb => verb, _ => new Hold(), StringComparer.OrdinalIgnoreCase);
        listener.Start();
        serving = ServeAsync(stopping.Token);
    }

    /// <summary>A server that aborts the connection with a reset, answering nothing, when the command arrives.</summary>
    public static SmtpTestServer Resetting(string verb) => new(null, verb, []);

    public string ConnectionString => $"smtp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>The addresses of every <c>RCPT TO</c>, as the client wrote them between the angle brackets.</summary>
    public IReadOnlyCollection<string> Recipients => recipients;

    public IReadOnlyCollection<string> Messages => messages;

    /// <summary>Completes when the held command has arrived.</summary>
    public Task ReceivedAsync(string verb) => holds[verb].Received.Task;

    public void Release(string verb) => holds[verb].Released.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        foreach (var hold in holds.Values)
        {
            hold.Released.TrySetResult();
        }

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
            if (holds.TryGetValue(verb, out var hold))
            {
                hold.Received.TrySetResult();
                await hold.Released.Task.WaitAsync(cancellationToken);
            }

            if (string.Equals(resetAt, verb, StringComparison.OrdinalIgnoreCase))
            {
                client.Client.LingerState = new LingerOption(enable: true, seconds: 0);
                client.Client.Close();
                return;
            }

            if (refusal is { } refused && string.Equals(refused.Verb, verb, StringComparison.OrdinalIgnoreCase))
            {
                var start = line.IndexOf('<', StringComparison.Ordinal);
                var end = line.LastIndexOf('>');
                var address = start >= 0 && end > start ? line[(start + 1)..end] : string.Empty;
                await writer.WriteLineAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, refused.Answer, address));
                continue;
            }

            switch (verb)
            {
                case "EHLO":
                case "HELO":
                    await writer.WriteLineAsync("250 test");
                    break;
                case "RCPT":
                    var start = line.IndexOf('<', StringComparison.Ordinal);
                    var end = line.LastIndexOf('>');
                    recipients.Enqueue(line[(start + 1)..end]);
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 Go ahead");
                    messages.Enqueue(await ReadDataAsync(reader, cancellationToken));
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

    private static async Task<string> ReadDataAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line && line != ".")
        {
            data.Append(line).Append("\r\n");
        }

        return data.ToString();
    }

    private sealed class Hold
    {
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
