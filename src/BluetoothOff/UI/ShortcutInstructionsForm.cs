namespace BluetoothOff.UI;

internal sealed class ShortcutInstructionsForm : Form
{
    internal ShortcutInstructionsForm(string endpoint)
    {
        Text = "Bluetooth Off — Apple Shortcut Instructions";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 600);
        MinimumSize = new Size(680, 520);

        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 10),
            Text = BuildInstructions(endpoint),
        };

        Controls.Add(text);
    }

    private static string BuildInstructions(string endpoint)
    {
        return $$"""
            TURN OFF PC BLUETOOTH

            1. Install Tailscale on the iPhone and sign in with the same account as this PC.
            2. In Apple Shortcuts, create "Turn Off PC Bluetooth".
            3. Add the Tailscale "Get Status" action. If disconnected, run Tailscale "Connect".
            4. Add "Show Alert" asking you to confirm turning Bluetooth off.
            5. Add "URL" with:
               {{endpoint}}/api/v1/bluetooth/off
            6. Add "Get Contents of URL":
               Method: POST
               Request body: none
               Header name: Authorization
               Header value: Bearer YOUR_TOKEN
            7. Read the returned Dictionary value "state" and show it in a notification.

            CHECK PC BLUETOOTH

            Repeat the steps without the confirmation alert, using:
               {{endpoint}}/api/v1/status
            Method: GET
            Header name: Authorization
            Header value: Bearer YOUR_TOKEN

            SECURITY NOTES

            - Never add the token to the URL.
            - Do not share or export the configured Shortcut; it contains the token.
            - Rotate the token from the Windows tray menu if the phone or Shortcut is exposed.
            - Tailscale must be connected, and the PC must be awake with this Windows user signed in.
            - There is intentionally no remote Bluetooth-on operation.
            """;
    }
}

