# Bluetooth Off

Bluetooth Off is a small Windows tray application that lets an authenticated Apple Shortcut read the PC's Bluetooth state and turn Bluetooth off through a private Tailscale connection.

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

## Build and install

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
