namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The test classes that invite or renew through the API as <c>owner</c>. Each such request counts on owner's one
/// invitation sender row (#265). Two requests at once no longer race on it: since #369 each holds the row until it
/// commits, and the first two create it once (docs/adr/0004). The collection stays because its tests read and seed owner's
/// row and tally, or delete it to start afresh, which another class's request at the same time would disturb, and
/// because a request of one class would wait on another's. In this collection they run one after the other; inside a
/// class the tests already do. A new class that invites or renews as <c>owner</c> joins it.
/// <see cref="OrganisationInvitationMailOutageTests"/> stays in <see cref="MailOutage"/>, which runs alone.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InvitationSenders
{
    public const string Name = "Invitation senders";
}
