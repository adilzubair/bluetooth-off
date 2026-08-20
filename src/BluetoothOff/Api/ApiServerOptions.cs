namespace BluetoothOff.Api;

internal sealed record ApiServerOptions(int Port)
{
    internal const int MinimumDynamicPort = 49152;
    internal const int MaximumDynamicPort = 65535;

    internal void Validate()
    {
        if (Port is < MinimumDynamicPort or > MaximumDynamicPort)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "Port must be in the dynamic/private range.");
        }
    }
}

