# Bluetooth Off

Bluetooth Off is a small Windows tray application that lets an authenticated Apple Shortcut read the PC's Bluetooth state and turn Bluetooth off through a private Tailscale connection.

[Download the latest Windows x64 release](https://github.com/adilzubair/bluetooth-off/releases/latest/download/BluetoothOff-win-x64.exe)

The application is intentionally narrow: it does not expose Bluetooth-on, arbitrary device control, PowerShell, shell commands, or public internet access.

## What it provides

- A self-contained Windows x64 tray application.
- A loopback-only authenticated API with Bluetooth status and off operations.
- Private HTTPS access through Tailscale Serve; Tailscale Funnel is rejected.
- Exact Host, Tailscale identity, and 256-bit bearer-token validation.
- One-time pairing-token display and immediate token rotation.
- Per-user installation and start-at-login without elevation or firewall changes.

## Security model

The local API listens only on the IPv4 loopback interface. Tailscale Serve terminates private HTTPS and forwards requests locally. Every request must also pass exact Host validation, Tailscale user identity validation, and bearer-token validation.

Generated tokens, machine configuration, logs, and publish artifacts must never be committed.

See [the threat model](docs/THREAT-MODEL.md) for the security boundary and residual risks.

## Download and install

Requirements: Windows 10 build 19041 or newer on x64, plus Tailscale on both the PC and iPhone.

- For portable use, download `BluetoothOff-win-x64.exe` from the latest release and run it directly.
- For automatic startup, download `BluetoothOff-win-x64.zip`, extract it, and run `Install.ps1` from PowerShell. This installs only for the current user and does not require administrator privileges.
- The release is currently not Authenticode-signed, so Windows SmartScreen may warn on first launch. Verify `SHA256SUMS.txt` and the GitHub build attestation before running the file.

The first-run wizard requests Windows Bluetooth permission and configures tailnet-private Tailscale Serve. It never enables public Tailscale Funnel.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
.\scripts\install.ps1
```

The publish script performs a locked restore and runs the test suite before producing the self-contained executable. Full instructions are in [docs/INSTALL.md](docs/INSTALL.md).

## iPhone setup

Follow [docs/APPLE-SHORTCUTS.md](docs/APPLE-SHORTCUTS.md) after the Windows setup wizard displays the one-time bearer token.

## API

- `GET /api/v1/status`
- `POST /api/v1/bluetooth/off` with no request body

Both endpoints require the private Tailscale HTTPS Host, the Tailscale identity injected by Serve, and `Authorization: Bearer <token>`.

## License

Bluetooth Off is available under the [MIT License](LICENSE).
