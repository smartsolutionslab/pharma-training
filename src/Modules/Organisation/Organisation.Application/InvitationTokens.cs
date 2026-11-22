using System.Buffers.Text;
using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>Creates the secret of an invitation link: 256 random bits, 43 URL-safe characters.</summary>
public static class InvitationTokens
{
    private const int Bytes = 32;

    public static InvitationToken Next() => InvitationToken.From(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes)));
}
