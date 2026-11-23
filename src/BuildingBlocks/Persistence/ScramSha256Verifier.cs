using System.Security.Cryptography;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// A password's SCRAM-SHA-256 verifier as PostgreSQL stores it in pg_authid (RFC 5802, RFC 7677):
/// <c>SCRAM-SHA-256$&lt;iterations&gt;:&lt;salt&gt;$&lt;StoredKey&gt;:&lt;ServerKey&gt;</c>, base64. Given one as a role's
/// PASSWORD, the server stores it as it is, whatever password_encryption says, and the clear password never reaches the
/// server, its statement logs or an error's context. A new salt each time unless one is given, so two verifiers of one
/// password differ.
/// </summary>
public static class ScramSha256Verifier
{
    // PostgreSQL's own (scram_iterations' default) and the salt length it uses.
    private const int Iterations = 4096;

    private const int SaltLength = 16;

    private const int KeyLength = 32;

    /// <summary>The verifier of <paramref name="password"/>, with a random salt.</summary>
    public static string Of(string password) => Of(password, RandomNumberGenerator.GetBytes(SaltLength));

    /// <summary>
    /// The verifier of <paramref name="password"/> with the given <paramref name="salt"/>, at PostgreSQL's 4096 iterations:
    /// the same password and salt give the same verifier, which is how it is checked against RFC 7677's example.
    /// </summary>
    public static string Of(string password, byte[] salt)
    {
        // SASLprep of printable ASCII is the identity; anything else PostgreSQL would normalize first, which this does not.
        Ensure.That(password, nameof(password))
            .IsNotNullOrWhiteSpace()
            .Satisfies(value => value.All(character => character is >= ' ' and <= '~'), "The password must be printable ASCII.");
        Ensure.That(salt, nameof(salt)).IsNotNull();
        Ensure.That(salt.Length, nameof(salt)).AtLeast(1);

        var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        var clientKey = HMACSHA256.HashData(salted, "Client Key"u8);
        var storedKey = SHA256.HashData(clientKey);
        var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        CryptographicOperations.ZeroMemory(salted);
        CryptographicOperations.ZeroMemory(clientKey);

        return $"SCRAM-SHA-256${Iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }
}
