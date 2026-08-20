using BluetoothOff.Domain;

namespace BluetoothOff.Services;

internal enum RadioAccessDecision
{
    Allowed,
    Denied,
}

internal enum RadioSetDecision
{
    Allowed,
    Denied,
}

internal interface IRadioPlatform
{
    Task<RadioAccessDecision> RequestAccessAsync(CancellationToken cancellationToken);

    Task<IRadioDevice?> FindBluetoothRadioAsync(CancellationToken cancellationToken);
}

internal interface IRadioDevice
{
    BluetoothState State { get; }

    Task<RadioSetDecision> SetOffAsync(CancellationToken cancellationToken);
}

