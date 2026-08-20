namespace BluetoothOff.Tailscale;

internal sealed class TailscaleServeConsentRequiredException : TailscaleException
{
    internal TailscaleServeConsentRequiredException(Uri activationUri)
        : base("Tailscale Serve needs one-time approval before Bluetooth Off can create private HTTPS access.")
    {
        ActivationUri = activationUri;
    }

    internal Uri ActivationUri { get; }
}
