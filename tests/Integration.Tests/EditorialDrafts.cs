namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The test classes that touch the editorial database's course versions, chapters, questions or answer options, or
/// count its deadlocks: they run one after the other, so no class's requests wait on another's gate or add to its
/// deadlock count. Membership follows who touches those tables, not who takes a gate: a gate that locks a whole table
/// (<see cref="EditorialDatabase.LockAnswerOptionsAsync"/>, or ACCESS EXCLUSIVE on the options) holds every request
/// that reads or writes it, whichever test sent it, and the gate's count of the backends behind it includes them. So
/// every editorial test class that creates, reads or changes a version, its chapters or its questions belongs in this
/// collection, whether or not it takes a gate itself.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EditorialDrafts
{
    public const string Name = "Editorial drafts";
}
