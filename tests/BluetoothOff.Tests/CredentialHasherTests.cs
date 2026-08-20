using BluetoothOff.Security;

namespace BluetoothOff.Tests;

[TestClass]
public sealed class CredentialHasherTests
{
    [TestMethod]
    public void CreateTokenCreatesA256BitCredential()
    {
        var credential = CredentialHasher.CreateToken();

        Assert.AreEqual(43, credential.Plaintext.Length);
        Assert.IsTrue(CredentialHasher.IsValidHash(credential.Sha256Hash));
        Assert.IsTrue(CredentialHasher.FixedTimeMatchesExact(
            credential.Plaintext,
            credential.Sha256Hash));
    }

    [TestMethod]
    public void LoginHashIsCaseInsensitiveAndTrimmed()
    {
        var hash = CredentialHasher.HashLogin("person@example.com");

        Assert.IsTrue(CredentialHasher.FixedTimeMatchesLogin(" PERSON@example.com ", hash));
    }

    [TestMethod]
    public void RotateTokenImmediatelyInvalidatesPreviousCredential()
    {
        var original = CredentialHasher.CreateToken();
        var state = new ApiSecurityState(
            "bluetooth-off-pc.example.ts.net",
            CredentialHasher.HashLogin("person@example.com"),
            original.Sha256Hash);

        var replacement = state.RotateToken();

        Assert.IsFalse(CredentialHasher.FixedTimeMatchesExact(
            original.Plaintext,
            state.Snapshot.TokenHash));
        Assert.IsTrue(CredentialHasher.FixedTimeMatchesExact(
            replacement.Plaintext,
            state.Snapshot.TokenHash));
    }
}

