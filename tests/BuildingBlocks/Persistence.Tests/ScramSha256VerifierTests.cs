using System.Security.Cryptography;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests;

/// <summary>
/// The verifier a module role's password reaches PostgreSQL as (docs/adr/0005), checked against the SCRAM-SHA-256 example
/// of RFC 7677, section 3: the user <c>user</c>, the password <c>pencil</c>, the salt <c>W22ZaJ0SNY7soEsUEjb6gQ==</c> and
/// 4096 iterations. The expected keys were computed apart from this code (Python's hashlib) and reproduce the RFC's
/// exchange: the client's proof and the server's signature below are the RFC's own, so a verifier holding other keys fails
/// the login it would have to verify.
/// </summary>
public class ScramSha256VerifierTests
{
    private const string Password = "pencil";

    private const string Salt = "W22ZaJ0SNY7soEsUEjb6gQ==";

    private const string Expected = "SCRAM-SHA-256$4096:W22ZaJ0SNY7soEsUEjb6gQ==$WG5d8oPm3OtcPnkdi4Uo7BkeZkBFzpcXkuLmtbsT4qY=:wfPLwcE6nTWhTAmQ7tl2KeoiWGPlZqQxSrmfPwDl2dU=";

    // RFC 7677's exchange: client-first-message-bare, server-first-message and client-final-message-without-proof, joined by commas.
    private const string AuthMessage =
        "n=user,r=rOprNGfwEbeRWgbNEkqO,"
        + "r=rOprNGfwEbeRWgbNEkqO%hvYDpWUa2RaTCAfuxFIlj)hNlF$k0,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4096,"
        + "c=biws,r=rOprNGfwEbeRWgbNEkqO%hvYDpWUa2RaTCAfuxFIlj)hNlF$k0";

    private const string ClientProof = "dHzbZapWIk4jUhN+Ute9ytag9zjfMHgsqmmiz7AndVQ=";

    private const string ServerSignature = "6rriTRBi23WpRR/wtup+mMhUZUn/dB5nLTJRsjl95G4=";

    [Fact]
    public void The_verifier_of_the_rfc_7677_example_is_the_one_the_rfc_implies()
    {
        var verifier = ScramSha256Verifier.Of(Password, Convert.FromBase64String(Salt));

        verifier.ShouldBe(Expected);
    }

    [Fact]
    public void The_verifiers_keys_verify_the_rfc_7677_exchange()
    {
        var verifier = ScramSha256Verifier.Of(Password, Convert.FromBase64String(Salt));

        var keys = verifier.Split('$')[2].Split(':');
        var storedKey = Convert.FromBase64String(keys[0]);
        var serverKey = Convert.FromBase64String(keys[1]);
        var authMessage = Encoding.UTF8.GetBytes(AuthMessage);

        // The server's check of the client: ClientKey = ClientProof XOR HMAC(StoredKey, AuthMessage), and H(ClientKey) = StoredKey.
        var clientSignature = HMACSHA256.HashData(storedKey, authMessage);
        var clientKey = Convert.FromBase64String(ClientProof).Zip(clientSignature, (proof, signature) => (byte)(proof ^ signature)).ToArray();
        Convert.ToBase64String(SHA256.HashData(clientKey)).ShouldBe(keys[0], "The StoredKey does not accept the RFC's client proof for 'pencil'.");
        // The client's check of the server: ServerSignature = HMAC(ServerKey, AuthMessage).
        Convert.ToBase64String(HMACSHA256.HashData(serverKey, authMessage)).ShouldBe(ServerSignature, "The ServerKey does not sign the RFC's exchange as the RFC's server does.");
    }
}
