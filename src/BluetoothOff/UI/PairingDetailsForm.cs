using BluetoothOff.Security;

namespace BluetoothOff.UI;

internal sealed class PairingDetailsForm : Form
{
    internal PairingDetailsForm(string endpoint, SecurityCredential credential)
    {
        Text = "Bluetooth Off — Pair iPhone";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 430);

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(24, 22),
            Text = "Save this token in Apple Shortcuts now",
        };

        var warning = new Label
        {
            Location = new Point(24, 52),
            Size = new Size(670, 48),
            Text = "The token is shown only in this window. Bluetooth Off stores only its SHA-256 hash. If you lose it, rotate the token from the tray menu.",
        };

        var endpointLabel = new Label
        {
            AutoSize = true,
            Location = new Point(24, 112),
            Text = "HTTPS endpoint",
        };

        var endpointBox = CreateReadOnlyBox(endpoint, new Point(24, 134), new Size(566, 27));
        var copyEndpoint = CreateCopyButton("Copy", endpoint, new Point(602, 132));

        var tokenLabel = new Label
        {
            AutoSize = true,
            Location = new Point(24, 178),
            Text = "Bearer token",
        };

        var tokenBox = CreateReadOnlyBox(credential.Plaintext, new Point(24, 200), new Size(566, 27));
        tokenBox.UseSystemPasswordChar = true;
        var showToken = new CheckBox
        {
            AutoSize = true,
            Location = new Point(24, 237),
            Text = "Show token",
        };
        showToken.CheckedChanged += (_, _) => tokenBox.UseSystemPasswordChar = !showToken.Checked;
        var copyToken = CreateCopyButton("Copy", credential.Plaintext, new Point(602, 198));

        var instructions = new Label
        {
            Location = new Point(24, 275),
            Size = new Size(670, 74),
            Text = "In Get Contents of URL, use the endpoint plus /api/v1/bluetooth/off, choose POST, leave the body empty, and add Authorization: Bearer <token>. Do not put the token in the URL or share the configured Shortcut.",
        };

        var close = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(554, 369),
            Size = new Size(140, 34),
            Text = "I saved the token",
        };

        AcceptButton = close;
        Controls.AddRange([
            title,
            warning,
            endpointLabel,
            endpointBox,
            copyEndpoint,
            tokenLabel,
            tokenBox,
            showToken,
            copyToken,
            instructions,
            close,
        ]);
    }

    private static TextBox CreateReadOnlyBox(string value, Point location, Size size)
    {
        return new TextBox
        {
            Location = location,
            ReadOnly = true,
            Size = size,
            Text = value,
        };
    }

    private static Button CreateCopyButton(string text, string value, Point location)
    {
        var button = new Button
        {
            Location = location,
            Size = new Size(92, 31),
            Text = text,
        };
        button.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(value);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(
                    "The clipboard is busy. Try again.",
                    "Bluetooth Off",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        return button;
    }
}

