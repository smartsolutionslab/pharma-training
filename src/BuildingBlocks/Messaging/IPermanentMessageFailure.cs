namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// Marks an exception a consumer throws when retrying its message cannot succeed until someone changes something by
/// hand: the host dead-letters the message on its first failure instead of retrying it (src/Host/Wiring/Messaging.cs,
/// docs/adr/0004). Any other exception is retried with backoff first. Implement it only on an exception that is certain
/// to fail the same way on every retry, never on one a dropped connection or a lost race can throw.
/// </summary>
public interface IPermanentMessageFailure;
