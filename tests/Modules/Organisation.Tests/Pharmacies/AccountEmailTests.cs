using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// The account's address as Keycloak reports it, with whether Keycloak verified it. Accepting an invitation carries it,
/// so whoever dispatches the command says whether the address is verified, and the handler decides.
/// </summary>
public class AccountEmailTests
{
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");

    [Fact]
    public void A_verified_address_holds_the_address_and_is_verified()
    {
        var email = AccountEmail.From(Anna, verified: true);

        email.Value.ShouldBe(Anna);
        email.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public void An_unverified_address_holds_the_address_and_is_not_verified()
    {
        var email = AccountEmail.From(Anna, verified: false);

        email.Value.ShouldBe(Anna);
        email.IsVerified.ShouldBeFalse();
    }

    [Fact]
    public void A_verified_and_an_unverified_account_of_one_address_are_not_the_same()
    {
        AccountEmail.From(Anna, verified: true).ShouldBe(AccountEmail.From(EmailAddress.From("Anna@Example.org"), verified: true));
        AccountEmail.From(Anna, verified: true).ShouldNotBe(AccountEmail.From(Anna, verified: false));
    }

    /// <summary>
    /// The aggregate never sees whether Keycloak verified the address; only the use case decides what an unverified one
    /// may do, so the type belongs to the Application, beside the command that carries it.
    /// </summary>
    [Fact]
    public void It_belongs_to_the_application_not_the_domain()
    {
        typeof(AccountEmail).Assembly.ShouldBe(typeof(OrganisationPolicyNames).Assembly);
        typeof(AccountEmail).Namespace.ShouldBe(typeof(OrganisationPolicyNames).Namespace);
    }

    [Fact]
    public void It_is_a_value_object_of_the_address()
    {
        var email = AccountEmail.From(Anna, verified: true);

        email.ShouldBeAssignableTo<IValueObject<EmailAddress>>().ShouldNotBeNull().Value.ShouldBe(Anna);
    }
}
