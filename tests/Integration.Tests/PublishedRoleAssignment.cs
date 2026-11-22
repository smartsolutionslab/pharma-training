namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// One role assignment the published Bicep declares: the file it is in, the role definition's id (lower case), and the
/// type and name of the resource it is scoped to (<c>resourceGroup</c> and empty where it names no scope).
/// </summary>
public sealed record PublishedRoleAssignment(string File, string Role, string ScopeType, string ScopeName);
