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

    [Fact]
    public void It_is_a_value_object_of_the_address()
    {
        var email = AccountEmail.From(Anna, verified: true);

        email.ShouldBeAssignableTo<IValueObject<EmailAddress>>().ShouldNotBeNull().Value.ShouldBe(Anna);
    }
}
