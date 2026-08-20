using BluetoothOff.Domain;
using Windows.Devices.Radios;

namespace BluetoothOff.Services;

internal sealed class WindowsRadioPlatform : IRadioPlatform
{
    public async Task<RadioAccessDecision> RequestAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = await Radio.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(true);
        return status == RadioAccessStatus.Allowed
            ? RadioAccessDecision.Allowed
            : RadioAccessDecision.Denied;
    }

    public async Task<IRadioDevice?> FindBluetoothRadioAsync(CancellationToken cancellationToken)
    {
        var radios = await Radio.GetRadiosAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var bluetooth = radios.FirstOrDefault(static radio => radio.Kind == RadioKind.Bluetooth);
        return bluetooth is null ? null : new WindowsRadioDevice(bluetooth);
    }

    private sealed class WindowsRadioDevice(Radio radio) : IRadioDevice
    {
        public BluetoothState State => radio.State switch
        {
            RadioState.On => BluetoothState.On,
            RadioState.Off => BluetoothState.Off,
            RadioState.Disabled => BluetoothState.Disabled,
            _ => BluetoothState.Unknown,
        };

        public async Task<RadioSetDecision> SetOffAsync(CancellationToken cancellationToken)
        {
            var status = await radio.SetStateAsync(RadioState.Off)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return status == RadioAccessStatus.Allowed
                ? RadioSetDecision.Allowed
                : RadioSetDecision.Denied;
        }
    }
}

