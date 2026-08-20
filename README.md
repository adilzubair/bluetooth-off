# Bluetooth Off

Bluetooth Off is a small Windows tray application that lets an authenticated Apple Shortcut read the PC's Bluetooth state and turn Bluetooth off through a private Tailscale connection.

The application is intentionally narrow: it does not expose Bluetooth-on, arbitrary device control, PowerShell, shell commands, or public internet access.

## Status

Implementation is in progress. Setup and usage instructions will be added as each verified milestone lands.

## Security model

The local API listens only on the IPv4 loopback interface. Tailscale Serve terminates private HTTPS and forwards requests locally. Every request must also pass exact Host validation, Tailscale user identity validation, and bearer-token validation.

Generated tokens, machine configuration, logs, and publish artifacts must never be committed.

