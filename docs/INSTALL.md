# Installation and first run

## Prerequisites

- Windows 10 build 19041 or later on x64 hardware.
- An iPhone with the Tailscale and Shortcuts apps.
- Both devices signed into the same Tailscale account.
- Permission to install the .NET 10 SDK for building. The installed application is self-contained and does not require the SDK at runtime.

## Windows setup

1. Install Tailscale:

   ```powershell
   winget install --id Tailscale.Tailscale --exact
   ```

2. Open Tailscale, sign in, and confirm it shows **Connected**.
3. In a PowerShell window in this repository, run:

   ```powershell
   .\scripts\install.ps1
   ```

4. Bluetooth Off appears in the Windows notification area. Its first-run wizard will:
   - verify Tailscale is connected;
   - refuse existing Funnel or Serve exposure rather than overwrite it;
   - set the Tailscale-only hostname to `bluetooth-off-pc`;
   - request Windows Bluetooth permission;
   - create a private Tailscale Serve HTTPS mapping to a loopback-only port;
   - show the Apple Shortcut bearer token once.
5. If the wizard reports that one-time Tailscale Serve approval is required, choose **Yes** to open the validated Tailscale activation page. Approve Serve, return to Bluetooth Off, and click **Run secure setup** again.

The HTTPS certificate causes the neutral `bluetooth-off-pc.<tailnet>.ts.net` hostname to appear in public certificate-transparency logs. It does not make the service public.

## Runtime behavior

- The tray application runs as the current user without elevation.
- A limited scheduled task starts it after sign-in and restarts it after a crash.
- Locking Windows does not stop it.
- It is unavailable while the PC is asleep, off, signed out, or waiting for the first sign-in after reboot.
- Kestrel listens only on `127.0.0.1`; the installer creates no inbound firewall rule.

## Updating

Run `scripts\install.ps1` again. Existing pairing configuration and logs under `%LOCALAPPDATA%\BluetoothOff` are preserved.

## Uninstalling

```powershell
.\scripts\uninstall.ps1
```

This removes the application and scheduled task. It removes Tailscale Serve only when the route still exactly belongs to Bluetooth Off. Configuration and logs are preserved by default.

To remove the preserved data too:

```powershell
.\scripts\uninstall.ps1 -PurgeData
```

Tailscale itself is never uninstalled by this script.
