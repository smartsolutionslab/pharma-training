using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace SmartSolutionsLab.PharmaTraining.Tests.Fakes;

/// <summary>
/// The model in tests: answers every request with the text it was given and keeps what it was asked. No test calls a
/// real model; a test that needs one to say something gives this client that answer, and reads what it was asked
/// from <see cref="Requests"/> and with which options from <see cref="Options"/>.
/// </summary>
public sealed class FakeChatClient(string answer) : IChatClient
{
    private readonly List<IReadOnlyList<ChatMessage>> requests = [];

    private readonly List<ChatOptions?> options = [];

    /// <summary>The messages of every request, in the order they came.</summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> Requests => requests;

    /// <summary>The options of every request, in the order they came.</summary>
    public IReadOnlyList<ChatOptions?> Options => options;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        requests.Add([.. messages]);
        this.options.Add(options);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = Metadata.DefaultModelId });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        Ensure.That(serviceType).IsNotNull();

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return Metadata;
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private static ChatClientMetadata Metadata { get; } = new("fake", defaultModelId: "fake-model");
}
