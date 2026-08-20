using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace BluetoothOff.Security;

internal static class CredentialHasher
{
    internal const int TokenByteLength = 32;

    internal static SecurityCredential CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var plaintext = WebEncoders.Base64UrlEncode(bytes);
        return new SecurityCredential(plaintext, HashExact(plaintext));
    }

    internal static string HashExact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    internal static string HashLogin(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return HashExact(value.Trim().ToUpperInvariant());
    }

    internal static bool FixedTimeMatchesExact(string value, string expectedHash)
    {
        return FixedTimeMatchesHash(HashExact(value), expectedHash);
    }

    internal static bool FixedTimeMatchesLogin(string value, string expectedHash)
    {
        return FixedTimeMatchesHash(HashLogin(value), expectedHash);
    }

    internal static bool IsValidHash(string value)
    {
        if (value.Length != SHA256.HashSizeInBytes * 2)
        {
            return false;
        }

        try
        {
            return Convert.FromHexString(value).Length == SHA256.HashSizeInBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeMatchesHash(string actualHash, string expectedHash)
    {
        if (!IsValidHash(expectedHash))
        {
            return false;
        }

        var actual = Convert.FromHexString(actualHash);
        var expected = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
