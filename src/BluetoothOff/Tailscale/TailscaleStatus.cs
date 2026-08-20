namespace BluetoothOff.Tailscale;

internal sealed record TailscaleStatus(
    string Version,
    bool IsConnected,
    string DnsName,
    string LoginName);

