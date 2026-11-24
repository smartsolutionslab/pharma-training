using SmartSolutionsLab.PharmaTraining.Host.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Host.Tests;

/// <summary>
/// Which module a broker message's type names (#230). Only a name whose module and event are made of
/// <c>[A-Za-z0-9_]</c> names one: a type with any other character, a NUL among them, is refused like an unknown type,
/// since PostgreSQL refuses to store it in the inbox and Wolverine would requeue the message without end.
/// </summary>
public class BrokerPublishersTests
{
    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";

    [Theory]
    [InlineData(Contracts + "Organisation.MembershipStartedV1", "Organisation")]
    [InlineData(Contracts + "Editorial.CourseVersionPublishedV1", "Editorial")]
    [InlineData(Contracts + "Some_Module2.Event_V2", "Some_Module2")]
    public void A_contracts_event_names_its_module(string typeName, string module)
    {
        BrokerPublishers.ModuleOf(typeName).ShouldBe(module);
    }

    [Theory]
    [InlineData(Contracts + "Organisation.MembershipStartedV1\0")]
    [InlineData(Contracts + "Organisation\0.MembershipStartedV1")]
    [InlineData(Contracts + "Organisation.Membership\0StartedV1")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1\n")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1\r")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1\t")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1\u001f")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1\u007f")]
    [InlineData(Contracts + "Organisation.Membership StartedV1")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1 ")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1-x")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1+Nested")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1, Contracts")]
    [InlineData(Contracts + "Organisätion.MembershipStartedV1")]
    [InlineData(Contracts + "Organisation.MitgliedschaftBegonnenÄ")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1 ")]
    [InlineData(Contracts + "Organisation.MembershipStartedV1​")]
    [InlineData(Contracts + "Organisation..MembershipStartedV1")]
    [InlineData(Contracts + ".MembershipStartedV1")]
    [InlineData(Contracts + "Organisation.")]
    [InlineData(Contracts + "Organisation")]
    [InlineData(Contracts + "Organisation.Memberships.StartedV1")]
    [InlineData("SmartSolutionsLab.PharmaTraining.Host.Messaging.ForgedPublisherException")]
    [InlineData("")]
    [InlineData(null)]
    public void A_type_that_is_no_plain_contracts_event_names_no_module(string? typeName)
    {
        BrokerPublishers.ModuleOf(typeName).ShouldBeNull($"ModuleOf accepted the type {Escaped(typeName)}.");
    }

    /// <summary>The type name with every character outside printable ASCII spelt as its code point, so a failure shows it.</summary>
    private static string Escaped(string? typeName) =>
        typeName is null
            ? "(null)"
            : string.Concat(typeName.Select(character => character is >= ' ' and <= '~' ? character.ToString() : $"\\u{(int)character:x4}"));
}
