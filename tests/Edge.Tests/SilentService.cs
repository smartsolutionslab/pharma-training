using System.Net;
using System.Net.Sockets;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// A service that accepts connections on a free local port and never answers, not even <c>100 Continue</c>: what the
/// edge meets when a service hangs before it reads a body. It keeps every byte it receives (<see cref="Received"/>).
/// </summary>
public sealed class SilentService : IDisposable
{
    private readonly TcpListener listener;

    private readonly List<Socket> connections = [];

    private readonly MemoryStream received = new();

    private SilentService(TcpListener listener)
    {
        this.listener = listener;
    }

    public string Address => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>Everything the service received so far, on every connection, as ASCII.</summary>
    public string Received
    {
        get
        {
            lock (received)
            {
                return System.Text.Encoding.ASCII.GetString(received.ToArray());
            }
        }
    }

    public static SilentService Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var service = new SilentService(listener);
        _ = service.AcceptAsync();
        return service;
    }

    public void Dispose()
    {
        listener.Stop();
        received.Dispose();
        lock (connections)
        {
            foreach (var connection in connections)
            {
                connection.Dispose();
            }
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var connection = await listener.AcceptSocketAsync();
                lock (connections)
                {
                    connections.Add(connection);
                }

                _ = ReceiveAsync(connection);
            }
        }
        catch (ObjectDisposedException)
        {
            // Stopped.
        }
        catch (SocketException)
        {
            // Stopped.
        }
    }

    private async Task ReceiveAsync(Socket connection)
    {
        var buffer = new byte[8_192];
        try
        {
            while (true)
            {
                var read = await connection.ReceiveAsync(buffer);
                if (read == 0)
                {
                    return;
                }

                lock (received)
                {
                    received.Write(buffer, 0, read);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Closed.
        }
        catch (SocketException)
        {
            // Closed.
        }
    }
}
