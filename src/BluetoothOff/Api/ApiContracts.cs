using BluetoothOff.Domain;

namespace BluetoothOff.Api;

internal sealed record StatusResponse(string State, DateTimeOffset ObservedAt);

internal sealed record OffResponse(
    string Requested,
    string State,
    bool Changed,
    DateTimeOffset ObservedAt);

internal sealed record ErrorResponse(string Code, string CorrelationId);

internal static class BluetoothStateWireFormat
{
    internal static string Format(BluetoothState state)
    {
        return state switch
        {
            BluetoothState.On => "on",
            BluetoothState.Off => "off",
            BluetoothState.Disabled => "disabled",
            _ => "unknown",
        };
    }
}

