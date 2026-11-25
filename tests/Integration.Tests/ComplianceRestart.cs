namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The tests that stop Compliance's service host. They run alone, after every parallel test, because the other
/// Compliance tests need it up; each of them starts it again before it ends, whatever happened.
/// </summary>
[CollectionDefinition(nameof(ComplianceRestart), DisableParallelization = true)]
public sealed class ComplianceRestart;
