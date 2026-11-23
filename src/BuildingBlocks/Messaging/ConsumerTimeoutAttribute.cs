namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// Gives a consumer (<c>*Consumer</c>) longer than the messaging library's default execution timeout (60 seconds), after
/// which the handling of a message is cancelled and the message retried. Put it on the handler method or on the consumer
/// class; the method's wins. It names no messaging library, so a module's Application project may use it; the host's
/// messaging turns it into the handler's timeout (<see cref="ConsumerTimeoutPolicy"/>). The timeout is per message:
/// whatever the consumer does for one message, all of it, must finish within it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ConsumerTimeoutAttribute : Attribute
{
    public ConsumerTimeoutAttribute(int minutes)
    {
        Ensure.That(minutes, nameof(minutes)).AtLeast(1);
        Minutes = minutes;
    }

    public int Minutes { get; }

    public TimeSpan Timeout => TimeSpan.FromMinutes(Minutes);
}
