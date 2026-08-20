using BluetoothOff.Api;
using BluetoothOff.Security;

namespace BluetoothOff.Configuration;

internal sealed record AppConfiguration(
    int Version,
    int LoopbackPort,
    string ExpectedHost,
    string ExpectedLoginHash,
    string TokenHash)
{
    internal const int CurrentVersion = 1;

    internal void Validate()
    {
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException("The application configuration version is not supported.");
        }

        new ApiServerOptions(LoopbackPort).Validate();

        try
        {
            _ = new ApiSecurityState(ExpectedHost, ExpectedLoginHash, TokenHash);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The application security configuration is invalid.", exception);
        }
    }
}

