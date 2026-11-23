namespace SmartSolutionsLab.PharmaTraining.Host.Messaging;

/// <summary>
/// An event from the broker that its publisher's broker user may not publish (<see cref="PublisherCheck"/>). Trying it
/// again cannot change who sent it, so it is dead-lettered without a retry; replaying it fails the same way.
/// </summary>
public sealed class ForgedPublisherException : Exception
{
    public ForgedPublisherException()
    {
    }

    public ForgedPublisherException(string message)
        : base(message)
    {
    }

    public ForgedPublisherException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
