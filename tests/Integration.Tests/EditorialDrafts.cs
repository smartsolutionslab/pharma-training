namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The test classes that hold an editorial version's rows or tables, or count the editorial database's deadlocks: they
/// run one after the other, so no class's requests wait on another's gate or add to its deadlock count.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EditorialDrafts
{
    public const string Name = "Editorial drafts";
}
