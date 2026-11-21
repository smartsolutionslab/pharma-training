using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>The realm's local users sign in with their names as passwords, and their access tokens carry a verified email and a name.</summary>
public class RealmUsersTests(AspireFixture aspire)
{
    [Theory]
    [InlineData("editor", "editor@example.com", "Emma Editor")]
    [InlineData("owner", "owner@example.com", "Olaf Owner")]
    [InlineData("employee-a", "employee-a@example.com", "Anna Employee")]
    [InlineData("employee-b", "employee-b@example.com", "Ben Employee")]
    public async Task A_local_user_signs_in_with_a_verified_email_and_a_name(string user, string email, string name)
    {
        var token = await aspire.RequestAccessTokenAsync(user);

        using var payload = Payload(token);

        payload.RootElement.GetProperty("email").GetString().ShouldBe(email);
        payload.RootElement.GetProperty("email_verified").ValueKind.ShouldBe(JsonValueKind.True);
        payload.RootElement.GetProperty("name").GetString().ShouldBe(name);
    }

    [Fact]
    public async Task The_editor_holds_the_editor_role_and_the_owner_does_not()
    {
        var editorToken = await aspire.RequestAccessTokenAsync("editor");
        var ownerToken = await aspire.RequestAccessTokenAsync("owner");

        using var editor = Payload(editorToken);
        using var owner = Payload(ownerToken);

        RealmRoles(editor).ShouldContain("editor");
        RealmRoles(owner).ShouldNotContain("editor");
    }

    private static JsonDocument Payload(string token)
    {
        var bytes = Base64Url.DecodeFromChars(token.Split('.')[1]);
        return JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
    }

    private static List<string> RealmRoles(JsonDocument payload)
    {
        List<string> roles = [];
        if (payload.RootElement.TryGetProperty("realm_access", out var realmAccess)
            && realmAccess.TryGetProperty("roles", out var granted))
        {
            foreach (var role in granted.EnumerateArray())
            {
                roles.Add(role.GetString() ?? string.Empty);
            }
        }

        return roles;
    }
}
