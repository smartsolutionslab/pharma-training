using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// A pharmacy and its members through Organisation: the manager registers it unless their pharmacies list it; an employee
/// who is not a member yet is invited, unless an invitation to their address is open, and accepts with the token of the
/// newest mail Mailpit holds for them.
/// </summary>
internal sealed class PharmacySeeding(EdgeClient edge, Mailbox mailbox, IReadOnlyDictionary<string, DemoUser> users, SeedCount count, TextWriter output)
{
    private readonly Dictionary<string, string> ids = [with(StringComparer.Ordinal)];

    public string IdOf(string pharmacy) => ids.TryGetValue(pharmacy, out var id) ? id : throw new SeedingException($"{pharmacy} is not in the seed.");

    public async Task EnsureAsync(DemoPharmacy pharmacy, CancellationToken cancellationToken)
    {
        var id = await MembershipAsync(pharmacy.Manager, pharmacy.Name, cancellationToken);
        if (id is null)
        {
            id = await edge.CreateAsync(pharmacy.Manager, "/organisation/pharmacies", new JsonObject { ["name"] = pharmacy.Name, ["jobFunction"] = "Pharmacist" }, cancellationToken);
            count.Add();
            output.WriteLine($"Demo data: registered {pharmacy.Name}.");
        }

        ids[pharmacy.Name] = id;
        IReadOnlyList<JsonNode>? open = null;
        foreach (var employee in pharmacy.Employees)
        {
            if (await MembershipAsync(employee.Username, pharmacy.Name, cancellationToken) is not null)
            {
                continue;
            }

            var address = users[employee.Username].Email;
            open ??= await edge.AllAsync(pharmacy.Manager, $"/organisation/pharmacies/{id}/invitations", cancellationToken);
            if (!open.Any(invitation => string.Equals(Json.Text(invitation, "email"), address, StringComparison.OrdinalIgnoreCase)))
            {
                var invitation = new JsonObject { ["email"] = address, ["jobFunction"] = employee.JobFunction.ToString(), ["role"] = "Employee" };
                await edge.CreateAsync(pharmacy.Manager, $"/organisation/pharmacies/{id}/invitations", invitation, cancellationToken);
            }

            var token = await mailbox.TokenAsync(address, pharmacy.Name, cancellationToken);
            await edge.CallAsync(employee.Username, HttpMethod.Post, "/organisation/invitations/accept", new JsonObject { ["token"] = token }, cancellationToken);
            count.Add();
        }
    }

    /// <summary>The pharmacy's id if the user is a member of it, by its name.</summary>
    private async Task<string?> MembershipAsync(string user, string pharmacy, CancellationToken cancellationToken)
    {
        var mine = await edge.AllAsync(user, "/organisation/pharmacies/mine", cancellationToken);
        return Json.Text(mine.FirstOrDefault(each => Json.Text(each, "name") == pharmacy), "id");
    }
}
