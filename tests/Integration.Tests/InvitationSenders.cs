namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The test classes that invite or renew through the API as <c>owner</c>. Each such request counts on owner's one
/// invitation sender row (#265), whose row version two requests at once race on, so one of them would answer 409 for
/// another class's request; the run's first two counts at once both insert the row, and the loser answers 409
/// <c>already-exists</c> (#366, docs/adr/0004). In this collection they run one after the other; inside a class the tests
/// already do. A new class that invites or renews as <c>owner</c> joins it.
/// <see cref="OrganisationInvitationMailOutageTests"/> stays in <see cref="MailOutage"/>, which runs alone.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InvitationSenders
{
    public const string Name = "Invitation senders";
}
