# Security Policy

## Reporting a vulnerability

Please use [GitHub private vulnerability reporting](https://github.com/adilzubair/bluetooth-off/security/advisories/new). Do not open a public issue for a suspected vulnerability until a fix is available.

Do not include bearer tokens, authorization headers, Tailscale login names, private tailnet hostnames, request bodies, query strings, or exported Apple Shortcuts in a report. Rotate the phone token immediately if it may have been exposed.

## Intended boundary

Bluetooth Off protects its two narrow operations—status and turn-off—from the public internet, local-network peers, unrelated tailnet users, browser cross-origin requests, and callers without the paired token.

The design assumes the Windows account, iPhone, Tailscale account, and Apple Shortcut remain trusted. Compromise of any of those is outside the application's security boundary.

## Non-goals

The project will not expose arbitrary commands, PowerShell, file access, Bluetooth-on, or a general device-control API. It will not create an inbound Windows Firewall rule or use Tailscale Funnel.

## Sensitive data

Only a cryptographic hash of the pairing token is stored on Windows. The plaintext token exists in the paired Apple Shortcut and is displayed once when created. Rotate the token immediately if the Shortcut or phone may have been exposed.

Do not include tokens, authorization headers, Tailscale login names, request bodies, or query strings in issue reports or logs.
