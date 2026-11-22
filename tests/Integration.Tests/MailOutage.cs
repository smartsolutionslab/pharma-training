namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The tests that stop Mailpit. They run alone, after every parallel test, because the other mail tests and the host's
/// health need Mailpit up; each of them starts it again before it ends, whatever happened.
/// </summary>
[CollectionDefinition(nameof(MailOutage), DisableParallelization = true)]
public sealed class MailOutage;
