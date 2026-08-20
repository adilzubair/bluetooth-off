namespace BluetoothOff.Domain;

internal sealed record BluetoothStatus(BluetoothState State, DateTimeOffset ObservedAt);

