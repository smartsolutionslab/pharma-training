using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// An SMTP server on the loopback interface, without TLS or authentication, that records the recipients and messages it
/// is given. It can hold its answer to any of the commands named when it is created until the test releases it, so a
/// test decides how long each step of a send takes.
/// </summary>
internal sealed class SmtpTestServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    private readonly CancellationTokenSource stopping = new();

    private readonly Dictionary<string, Hold> holds;

    private readonly ConcurrentQueue<string> recipients = new();

    private readonly ConcurrentQueue<string> messages = new();

    private readonly Task serving;

    public SmtpTestServer(params string[] held)
    {
        holds = held.ToDictionary(verb => verb, _ => new Hold(), StringComparer.OrdinalIgnoreCase);
        listener.Start();
        serving = ServeAsync(stopping.Token);
    }

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
