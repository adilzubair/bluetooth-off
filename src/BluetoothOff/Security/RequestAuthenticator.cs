using Microsoft.Extensions.Primitives;

namespace BluetoothOff.Security;

internal sealed class RequestAuthenticator(ApiSecurityState state)
{
    internal const string TailscaleLoginHeader = "Tailscale-User-Login";
    private const string BearerPrefix = "Bearer ";

    internal bool IsAuthorized(HttpRequest request)
    {
        var snapshot = state.Snapshot;
        if (!HostMatches(request.Host, snapshot.ExpectedHost))
        {
            return false;
        }

        if (!TryGetSingleHeader(request.Headers, TailscaleLoginHeader, out var login)
            || !CredentialHasher.FixedTimeMatchesLogin(login, snapshot.ExpectedLoginHash))
        {
            return false;
        }

        if (!TryGetSingleHeader(request.Headers, "Authorization", out var authorization)
            || !authorization.StartsWith(BearerPrefix, StringComparison.Ordinal)
            || authorization.Length <= BearerPrefix.Length)
        {
            return false;
        }

        var token = authorization[BearerPrefix.Length..];
        return !token.Contains(' ')
            && !token.Contains(',')
            && CredentialHasher.FixedTimeMatchesExact(token, snapshot.TokenHash);
    }

    private static bool HostMatches(HostString actual, string expectedHost)
    {
        return string.Equals(actual.Host, expectedHost, StringComparison.OrdinalIgnoreCase)
            && (actual.Port is null or 443);
    }

    private static bool TryGetSingleHeader(
        IHeaderDictionary headers,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(name, out StringValues values) || values.Count != 1)
        {
            return false;
        }

        var candidate = values[0];
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains('\r') || candidate.Contains('\n'))
        {
            return false;
        }

        value = candidate;
        return true;
    }
}

