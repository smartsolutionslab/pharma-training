using System.Text.Json;
using Aspire.Hosting.Testing;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The tests' way into the application's RabbitMQ: listen to what a service announces on a queue of the test's own,
/// or send an event as a publishing service's Wolverine does. It never declares an exchange, so it never conflicts
/// with the arguments Wolverine gives one.
/// </summary>
public static class Broker
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>A connection of the test's own to the broker the application runs on.</summary>
    public static async Task<IConnection> ConnectAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("messaging", cancellationToken)
            ?? throw new InvalidOperationException("The broker has no connection string.");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        return await factory.CreateConnectionAsync(cancellationToken);
    }

    /// <summary>
    /// Waits until the exchange exists, asking for it without declaring it. Wolverine declares a publisher's exchange
    /// when it first sends to it and a consumer's at startup.
    /// </summary>
    public static async Task WaitForExchangeAsync(IConnection connection, string exchange, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            // A passive declare of a missing exchange closes its channel, so every attempt gets a channel of its own.
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            try
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
                return;
            }
            catch (OperationInterruptedException) when (TimeProvider.System.GetElapsedTime(started) < WaitTimeout)
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationInterruptedException missing)
            {
                throw new TimeoutException($"The exchange {exchange} did not exist after {WaitTimeout.TotalSeconds} s.", missing);
            }
        }
    }

    /// <summary>A queue of the test's own, named by the broker and gone with the connection, bound to the event's exchange.</summary>
    public static async Task<string> BindQueueAsync(IChannel channel, string exchange, CancellationToken cancellationToken)
    {
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);

        // Wolverine's exchanges fan out; "#" also matches every routing key should one be a topic exchange.
        await channel.QueueBindAsync(queue.QueueName, exchange, routingKey: "#", cancellationToken: cancellationToken);
        return queue.QueueName;
    }

    /// <summary>The first message on the queue that is the one wanted; the ones before it are taken off the queue.</summary>
    public static async Task<T> ReceiveAsync<T>(IChannel channel, string queue, Func<T, bool> wanted, CancellationToken cancellationToken) =>
        (await ReceiveUntilAsync(channel, queue, wanted, cancellationToken))[^1];

    /// <summary>
    /// Every message on the queue, in the order the broker delivers them, up to and including the first that is the
    /// last one wanted. A test that sends a last message after the ones it counts gets every one sent before it.
    /// </summary>
    public static async Task<IReadOnlyList<T>> ReceiveUntilAsync<T>(IChannel channel, string queue, Func<T, bool> last, CancellationToken cancellationToken)
    {
        List<T> received = [];
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var delivery = await channel.BasicGetAsync(queue, autoAck: true, cancellationToken);
            if (delivery is null)
            {
                if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
                {
                    throw new TimeoutException($"The wanted message did not reach the queue within {WaitTimeout.TotalSeconds} s; {received.Count} others did.");
                }

                await Task.Delay(PollInterval, cancellationToken);
                continue;
            }

            var message = JsonSerializer.Deserialize<T>(delivery.Body.Span, JsonSerializerOptions.Web);
            if (message is null)
            {
                continue;
            }

            received.Add(message);
            if (last(message))
            {
                return received;
            }
        }
    }

    /// <summary>
    /// The first message on the queue that is the one wanted, with the AMQP <c>message-id</c> it arrived under; the
    /// ones before it are taken off the queue. Wolverine puts the envelope id there (see <see cref="SendAsync"/>), so a
    /// fanout copy on a queue of the test's own carries the id that a consuming service's inbox stores for its copy.
    /// </summary>
    public static async Task<(Guid MessageId, JsonElement Body)> ReceiveWithMessageIdAsync(IChannel channel, string queue, Func<JsonElement, bool> wanted, CancellationToken cancellationToken)
    {
        Ensure.That(channel).IsNotNull();
        Ensure.That(wanted).IsNotNull();

        var received = 0;
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var delivery = await channel.BasicGetAsync(queue, autoAck: true, cancellationToken);
            if (delivery is null)
            {
                if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
                {
                    throw new TimeoutException($"The wanted message did not reach the queue within {WaitTimeout.TotalSeconds} s; {received} others did.");
                }

                await Task.Delay(PollInterval, cancellationToken);
                continue;
            }

            received++;
            var body = JsonSerializer.Deserialize<JsonElement>(delivery.Body.Span, JsonSerializerOptions.Web);
            if (wanted(body))
            {
                var messageId = delivery.BasicProperties.MessageId
                    ?? throw new InvalidOperationException($"The wanted message arrived without a message-id: {body.GetRawText()}");
                return (Guid.Parse(messageId), body);
            }
        }
    }

    /// <summary>
    /// Sends an event as a publishing service's Wolverine does: with the credentials of the service whose module declares
    /// the event (<c>Contracts.&lt;Module&gt;</c>), to the fanout exchange named after its type, which its consumer's
    /// Wolverine declares, with the type, the envelope id and that service's user as <c>user_id</c> in the message's
    /// properties and the event as camelCase JSON. A consumer refuses an event no user of its module sent (docs/adr/0011).
    /// </summary>
    public static async Task SendAsync<T>(AspireFixture aspire, string type, T message, Guid messageId, CancellationToken cancellationToken)
    {
        await using (var admin = await ConnectAsync(aspire, cancellationToken))
        {
            await WaitForExchangeAsync(admin, type, cancellationToken);
        }

        var publisher = await BrokerUsers.ConnectionStringOfAsync(aspire, PublisherOf(type), cancellationToken);
        await using var connection = await BrokerUsers.ConnectAsync(publisher, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var properties = new BasicProperties
        {
            Type = type,
            ContentType = "application/json",
            MessageId = messageId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
            UserId = BrokerUsers.UserOf(publisher),
        };

        await channel.BasicPublishAsync(type, routingKey: string.Empty, mandatory: false, properties, JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), cancellationToken);
    }

    /// <summary>The service of the module that declares the event: <c>editorial</c> for <c>Contracts.Editorial.…</c>.</summary>
    private static string PublisherOf(string type)
    {
        const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";
        type.ShouldStartWith(Contracts, customMessage: $"{type} is no integration event of a module.");
        return type[Contracts.Length..type.IndexOf('.', Contracts.Length)].ToLowerInvariant();
    }
}
