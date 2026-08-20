namespace BluetoothOff.Security;

internal sealed class ApiSecurityState
{
    private readonly object _gate = new();
    private SecuritySnapshot _snapshot;

    internal ApiSecurityState(string expectedHost, string expectedLoginHash, string tokenHash)
    {
        _snapshot = CreateSnapshot(expectedHost, expectedLoginHash, tokenHash);
    }

    internal SecuritySnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    internal SecurityCredential RotateToken()
    {
        var credential = CredentialHasher.CreateToken();

        lock (_gate)
        {
            _snapshot = _snapshot with { TokenHash = credential.Sha256Hash };
        }

        return credential;
    }

    internal void Update(string expectedHost, string expectedLoginHash, string tokenHash)
    {
        var replacement = CreateSnapshot(expectedHost, expectedLoginHash, tokenHash);

        lock (_gate)
        {
            _snapshot = replacement;
        }
    }

    private static SecuritySnapshot CreateSnapshot(
        string expectedHost,
        string expectedLoginHash,
        string tokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHost);

        if (Uri.CheckHostName(expectedHost) != UriHostNameType.Dns || expectedHost.Contains(':'))
        {
            throw new ArgumentException("Expected host must be a DNS hostname without a port.", nameof(expectedHost));
        }

        if (!CredentialHasher.IsValidHash(expectedLoginHash))
        {
            throw new ArgumentException("Expected login hash is invalid.", nameof(expectedLoginHash));
        }

        if (!CredentialHasher.IsValidHash(tokenHash))
        {
            throw new ArgumentException("Token hash is invalid.", nameof(tokenHash));
        }

        return new SecuritySnapshot(expectedHost.ToLowerInvariant(), expectedLoginHash, tokenHash);
    }
}

internal sealed record SecuritySnapshot(
    string ExpectedHost,
    string ExpectedLoginHash,
    string TokenHash);

