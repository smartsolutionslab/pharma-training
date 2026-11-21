using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>The editorial module's policy editorial.edit requires the realm role editor, which editor holds and owner does not.</summary>
public class EditorialEditorRoleTests(AspireFixture aspire)
{
    private static readonly Uri EditPermission = new("/editorial/permissions/edit", UriKind.Relative);

    [Fact]
    public async Task A_user_with_the_editor_role_is_allowed()
    {
        using var client = await aspire.CreateHostClientAsync("editor");

        using var response = await client.GetAsync(EditPermission, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_pharmacy_owner_is_forbidden()
    {
        using var client = await aspire.CreateHostClientAsync("owner");

        using var response = await client.GetAsync(EditPermission, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(EditPermission, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
