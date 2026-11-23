using System.Security.Cryptography;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// A password's SCRAM-SHA-256 verifier as PostgreSQL stores it in pg_authid (RFC 5802, RFC 7677):
/// <c>SCRAM-SHA-256$&lt;iterations&gt;:&lt;salt&gt;$&lt;StoredKey&gt;:&lt;ServerKey&gt;</c>, base64. Given one as a role's
/// PASSWORD, the server stores it as it is, whatever password_encryption says, and the clear password never reaches the
/// server, its statement logs or an error's context. A new salt each time, so two verifiers of one password differ.
/// </summary>
public static class ScramSha256Verifier
{
    // PostgreSQL's own (scram_iterations' default) and the salt length it uses.
    private const int Iterations = 4096;

    private const int SaltLength = 16;

    private const int KeyLength = 32;

    /// <summary>The verifier of <paramref name="password"/>, with a random salt.</summary>
    public static string Of(string password)
    {
        // SASLprep of printable ASCII is the identity; anything else PostgreSQL would normalize first, which this does not.
        Ensure.That(password, nameof(password))
            .IsNotNullOrWhiteSpace()
            .Satisfies(value => value.All(character => character is >= ' ' and <= '~'), "The password must be printable ASCII.");

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        var clientKey = HMACSHA256.HashData(salted, "Client Key"u8);
        var storedKey = SHA256.HashData(clientKey);
        var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        CryptographicOperations.ZeroMemory(salted);
        CryptographicOperations.ZeroMemory(clientKey);

        return $"SCRAM-SHA-256${Iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }
}
