namespace BluetoothOff.Domain;

internal sealed class BluetoothControlException : Exception
{
    internal BluetoothControlException(BluetoothFailureCode code, string message)
        : base(message)
    {
        Code = code;
    }

    internal BluetoothFailureCode Code { get; }
}

