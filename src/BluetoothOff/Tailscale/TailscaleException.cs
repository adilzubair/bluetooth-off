namespace BluetoothOff.Tailscale;

internal class TailscaleException : Exception
{
    internal TailscaleException(string message)
        : base(message)
    {
    }

    internal TailscaleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
