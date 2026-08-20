namespace BluetoothOff.Domain;

internal sealed record BluetoothOffResult(
    BluetoothState State,
    bool Changed,
    DateTimeOffset ObservedAt);

