using System.Diagnostics;
using BluetoothOff.Api;
using BluetoothOff.Configuration;
using BluetoothOff.Domain;
using BluetoothOff.Logging;
using BluetoothOff.Security;
using BluetoothOff.Services;
using BluetoothOff.Tailscale;

namespace BluetoothOff.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppPaths _paths = new();
    private readonly AppConfigurationStore _configurationStore;
    private readonly BluetoothRadioController _radio;
    private readonly TailscaleClient _tailscale = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly BoundedFileLoggerProvider _loggerProvider;
    private ApiServer? _apiServer;
    private ApiSecurityState? _securityState;
    private AppConfiguration? _configuration;
    private bool _initializationStarted;
    private bool _exiting;

    internal TrayApplicationContext()
    {
        _configurationStore = new AppConfigurationStore(_paths);
        _radio = new BluetoothRadioController(new WindowsRadioPlatform());
        _loggerProvider = new BoundedFileLoggerProvider(_paths.LogsDirectory);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Bluetooth status", null, ShowBluetoothStatus);
        menu.Items.Add("Verify secure connection", null, VerifySecureConnection);
        menu.Items.Add("Apple Shortcut instructions", null, ShowShortcutInstructions);
        menu.Items.Add("Rotate phone token", null, RotateToken);
        menu.Items.Add("Run setup", null, RunSetup);
        menu.Items.Add("Open logs", null, OpenLogs);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, ExitApplication);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = SystemIcons.Shield,
            Text = "Bluetooth Off — starting",
            Visible = true,
        };
        _notifyIcon.DoubleClick += ShowBluetoothStatus;
        System.Windows.Forms.Application.Idle += InitializeOnFirstIdle;
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _radio.Dispose();
        _loggerProvider.Dispose();
        base.ExitThreadCore();
    }

    private async void InitializeOnFirstIdle(object? sender, EventArgs eventArgs)
    {
        if (_initializationStarted)
        {
            return;
        }

        _initializationStarted = true;
        System.Windows.Forms.Application.Idle -= InitializeOnFirstIdle;

        try
        {
            _configuration = await _configurationStore.LoadAsync(CancellationToken.None);
            if (_configuration is null)
            {
                await ShowSetupAsync();
            }
            else
            {
                await StartRuntimeAsync(_configuration);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or BluetoothControlException
            or InvalidOperationException)
        {
            SetTrayState("Bluetooth Off — attention needed");
            MessageBox.Show(
                exception.Message,
                "Bluetooth Off",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task ShowSetupAsync()
    {
        using var form = new SetupForm(_tailscale, _radio, _configurationStore);
        if (form.ShowDialog() != DialogResult.OK || form.Configuration is null)
        {
            SetTrayState("Bluetooth Off — setup required");
            return;
        }

        _configuration = form.Configuration;
        await StartRuntimeAsync(_configuration);
    }

    private async Task StartRuntimeAsync(AppConfiguration configuration)
    {
        if (!_radio.IsAuthorized)
        {
            await _radio.AuthorizeAsync(CancellationToken.None);
        }

        _securityState = new ApiSecurityState(
            configuration.ExpectedHost,
            configuration.ExpectedLoginHash,
            configuration.TokenHash);
        _apiServer = new ApiServer(
            new ApiServerOptions(configuration.LoopbackPort),
            _radio,
            _securityState,
            _loggerProvider);
        await _apiServer.StartAsync(CancellationToken.None);
        SetTrayState("Bluetooth Off — secure API running");
    }

    private async void ShowBluetoothStatus(object? sender, EventArgs eventArgs)
    {
        try
        {
            var status = await _radio.GetStatusAsync(CancellationToken.None);
            MessageBox.Show(
                string.Concat("Bluetooth is ", BluetoothStateWireFormat.Format(status.State), "."),
                "Bluetooth Off",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (BluetoothControlException exception)
        {
            MessageBox.Show(exception.Message, "Bluetooth Off", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void VerifySecureConnection(object? sender, EventArgs eventArgs)
    {
        if (_configuration is null)
        {
            MessageBox.Show("Run setup first.", "Bluetooth Off", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var status = await _tailscale.GetStatusAsync(CancellationToken.None);
            if (!status.IsConnected
                || !string.Equals(status.DnsName, _configuration.ExpectedHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new TailscaleException("Tailscale is disconnected or the configured hostname changed.");
            }

            await _tailscale.VerifyPrivateServeAsync(_configuration.LoopbackPort, CancellationToken.None);
            MessageBox.Show(
                "Tailscale is connected, Funnel is disabled, and Serve targets the expected loopback port.",
                "Bluetooth Off",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (TailscaleException exception)
        {
            MessageBox.Show(exception.Message, "Bluetooth Off", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowShortcutInstructions(object? sender, EventArgs eventArgs)
    {
        if (_configuration is null)
        {
            MessageBox.Show("Run setup first.", "Bluetooth Off", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ShortcutInstructionsForm(SetupForm.BuildEndpoint(_configuration));
        form.ShowDialog();
    }

    private async void RotateToken(object? sender, EventArgs eventArgs)
    {
        if (_configuration is null || _securityState is null)
        {
            MessageBox.Show("Run setup first.", "Bluetooth Off", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            "Rotate the phone token? The current Apple Shortcuts will stop working immediately.",
            "Bluetooth Off",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var credential = CredentialHasher.CreateToken();
            var replacement = _configuration with { TokenHash = credential.Sha256Hash };
            await _configurationStore.SaveAsync(replacement, CancellationToken.None);
            _securityState.Update(
                replacement.ExpectedHost,
                replacement.ExpectedLoginHash,
                replacement.TokenHash);
            _configuration = replacement;

            using var form = new PairingDetailsForm(SetupForm.BuildEndpoint(replacement), credential);
            form.ShowDialog();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            MessageBox.Show(exception.Message, "Bluetooth Off", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void RunSetup(object? sender, EventArgs eventArgs)
    {
        if (_configuration is not null)
        {
            MessageBox.Show(
                "Setup is already complete. Use Verify secure connection or Rotate phone token. Existing Tailscale Serve configuration will not be overwritten.",
                "Bluetooth Off",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        await ShowSetupAsync();
    }

    private void OpenLogs(object? sender, EventArgs eventArgs)
    {
        Directory.CreateDirectory(_paths.LogsDirectory);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.LogsDirectory,
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(_paths.LogsDirectory, "Bluetooth Off logs");
        }
    }

    private async void ExitApplication(object? sender, EventArgs eventArgs)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        if (_apiServer is not null)
        {
            await _apiServer.StopAsync(CancellationToken.None);
            await _apiServer.DisposeAsync();
            _apiServer = null;
        }

        ExitThread();
    }

    private void SetTrayState(string text)
    {
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }
}

