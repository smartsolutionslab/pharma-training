namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The test classes that invite or renew through the API as <c>owner</c>. Each such request counts on owner's one
/// invitation sender row (#265), whose row version two requests at once race on, so one of them would answer 409 for
/// another class's request. In this collection they run one after the other; inside a class the tests already do.
/// <see cref="OrganisationInvitationMailOutageTests"/> stays in <see cref="MailOutage"/>, which runs alone.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InvitationSenders
{
    public const string Name = "Invitation senders";
}
