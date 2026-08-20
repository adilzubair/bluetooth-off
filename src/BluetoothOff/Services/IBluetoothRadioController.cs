using BluetoothOff.Domain;

namespace BluetoothOff.Services;

internal interface IBluetoothRadioController
{
    bool IsAuthorized { get; }

    Task AuthorizeAsync(CancellationToken cancellationToken);

    Task<BluetoothStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<BluetoothOffResult> TurnOffAsync(CancellationToken cancellationToken);
}

