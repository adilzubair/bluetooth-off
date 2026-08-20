using System.Diagnostics;
using BluetoothOff.Configuration;
using BluetoothOff.Security;
using BluetoothOff.Services;
using BluetoothOff.Tailscale;

namespace BluetoothOff.UI;

internal sealed class SetupForm : Form
{
    private readonly TailscaleClient _tailscale;
    private readonly IBluetoothRadioController _radio;
    private readonly AppConfigurationStore _configurationStore;
    private readonly Label _statusLabel;
    private readonly Button _setupButton;

    internal SetupForm(
        TailscaleClient tailscale,
        IBluetoothRadioController radio,
        AppConfigurationStore configurationStore)
    {
        _tailscale = tailscale;
        _radio = radio;
        _configurationStore = configurationStore;

        Text = "Bluetooth Off — Secure Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(680, 420);

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(24, 24),
            Text = "Set up private iPhone access",
        };

        var explanation = new Label
        {
            Location = new Point(24, 58),
            Size = new Size(630, 105),
            Text = "Before continuing, install Tailscale on Windows and the iPhone, sign both into the same account, and connect them. Setup will use the neutral Tailscale name 'bluetooth-off-pc', request Windows Bluetooth permission, verify Funnel is disabled, and create a private HTTPS Serve mapping.",
        };

        var downloadButton = new Button
        {
            Location = new Point(24, 177),
            Size = new Size(178, 34),
            Text = "Open Tailscale download",
        };
        downloadButton.Click += OpenTailscaleDownload;

        _setupButton = new Button
        {
            Location = new Point(216, 177),
            Size = new Size(178, 34),
            Text = "Run secure setup",
        };
        _setupButton.Click += RunSetup;

        _statusLabel = new Label
        {
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(24, 230),
            Padding = new Padding(10),
            Size = new Size(630, 112),
            Text = _tailscale.IsInstalled
                ? "Tailscale was found. Make sure it is signed in and connected, then run setup."
                : "Tailscale was not found. Install it, sign in, then reopen or retry setup.",
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(534, 365),
            Size = new Size(120, 32),
            Text = "Not now",
        };

        CancelButton = cancel;
        Controls.AddRange([title, explanation, downloadButton, _setupButton, _statusLabel, cancel]);
    }

    internal AppConfiguration? Configuration { get; private set; }

    internal SecurityCredential? Credential { get; private set; }

    private static void OpenTailscaleDownload(object? sender, EventArgs eventArgs)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://tailscale.com/download/windows",
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                "Open https://tailscale.com/download/windows in your browser.",
                "Bluetooth Off",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private async void RunSetup(object? sender, EventArgs eventArgs)
    {
        _setupButton.Enabled = false;
        UseWaitCursor = true;
        _statusLabel.Text = "Checking Tailscale and Windows Bluetooth permission…";

        try
        {
            if (!_tailscale.IsInstalled)
            {
                throw new TailscaleException("Tailscale is not installed yet.");
            }

            var status = await _tailscale.GetStatusAsync(CancellationToken.None);
            if (!status.IsConnected)
            {
                throw new TailscaleException("Tailscale is installed but is not connected.");
            }

            await _tailscale.EnsureNoExistingExposureAsync(CancellationToken.None);
            await _tailscale.SetNeutralHostnameAsync(CancellationToken.None);
            status = await WaitForNeutralStatusAsync();

            if (!status.DnsName.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase))
            {
                throw new TailscaleException("Tailscale did not provide a valid MagicDNS HTTPS hostname.");
            }

            _statusLabel.Text = "Approve the Windows Bluetooth permission prompt if it appears…";
            await _radio.AuthorizeAsync(CancellationToken.None);

            var port = LoopbackPortSelector.SelectAvailablePort();
            var credential = CredentialHasher.CreateToken();
            var configuration = new AppConfiguration(
                AppConfiguration.CurrentVersion,
                port,
                status.DnsName.ToLowerInvariant(),
                CredentialHasher.HashLogin(status.LoginName),
                credential.Sha256Hash);

            _statusLabel.Text = "Creating and verifying private Tailscale HTTPS access…";
            var serveConfigured = false;
            try
            {
                await _tailscale.ConfigurePrivateServeAsync(port, CancellationToken.None);
                serveConfigured = true;
                await _tailscale.VerifyPrivateServeAsync(port, CancellationToken.None);
                await _configurationStore.SaveAsync(configuration, CancellationToken.None);
            }
            catch
            {
                if (serveConfigured)
                {
                    await _tailscale.DisableOwnedServeAsync(port, CancellationToken.None);
                }

                throw;
            }

            Configuration = configuration;
            Credential = credential;
            _statusLabel.Text = "Secure setup completed.";

            using (var pairing = new PairingDetailsForm(BuildEndpoint(configuration), credential))
            {
                pairing.ShowDialog(this);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (TailscaleServeConsentRequiredException exception)
        {
            const string status = "Enable Tailscale Serve in the browser, then run secure setup again.";
            _statusLabel.Text = status;

            var result = MessageBox.Show(
                string.Concat(
                    "Tailscale requires one-time approval to enable private Serve access for this tailnet. ",
                    "Open Tailscale's secure activation page now?\n\n",
                    "After approving it, return here and click Run secure setup again."),
                "Enable Tailscale Serve",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                OpenUrl(exception.ActivationUri.AbsoluteUri);
            }
        }
        catch (Exception exception) when (exception is TailscaleException
            or BluetoothOff.Domain.BluetoothControlException
            or IOException
            or InvalidOperationException)
        {
            _statusLabel.Text = exception.Message;
            MessageBox.Show(
                exception.Message,
                "Bluetooth Off setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            _setupButton.Enabled = true;
        }
    }

    private async Task<TailscaleStatus> WaitForNeutralStatusAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var status = await _tailscale.GetStatusAsync(CancellationToken.None);
            if (status.DnsName.StartsWith(
                string.Concat(TailscaleClient.NeutralHostname, "."),
                StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TailscaleException("The neutral Tailscale hostname did not become active in time.");
    }

    internal static string BuildEndpoint(AppConfiguration configuration)
    {
        return string.Concat("https://", configuration.ExpectedHost);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                "Windows could not open the browser. Open Tailscale from its system tray icon and enable Serve, then retry setup.",
                "Bluetooth Off",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
