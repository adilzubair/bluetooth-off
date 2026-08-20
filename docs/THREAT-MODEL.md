# Threat model

## Protected capability

The application exposes only two capabilities: read Bluetooth radio state and request the off state. It cannot turn Bluetooth on, execute commands, select arbitrary devices, or access files.

## Controls

- **Public internet and LAN peers:** Kestrel binds only to IPv4 loopback. No firewall rule or router forwarding is created. Tailscale Funnel is forbidden.
- **Transport security:** Tailscale provides authenticated encrypted networking; Serve adds a trusted HTTPS endpoint within the tailnet.
- **Unrelated tailnet users:** every request must carry the expected `Tailscale-User-Login` identity injected by Serve.
- **Stolen tailnet access:** every request additionally requires a 256-bit bearer token stored in the Apple Shortcut.
- **Database/configuration disclosure:** Windows stores only SHA-256 hashes of the token and normalized Tailscale login.
- **Header spoofing:** Tailscale strips incoming identity headers before adding trusted values. The backend listens only on loopback and still requires the bearer token.
- **DNS rebinding and browser requests:** the API validates the exact Host, provides no CORS policy, accepts no URL credentials, and requires a non-simple Authorization header.
- **Accidental or abusive requests:** off uses POST, accepts no request body, is rate limited, serialized, idempotent, and must be confirmed by observing the final radio state.
- **Information disclosure:** errors expose stable codes and correlation IDs, not exception details. Logs exclude framework request logging, headers, bodies, query strings, tokens, and Tailscale login names.
- **Supply chain:** dependencies restore from a repository-local NuGet.org-only source with committed lock files and NuGet auditing enabled.

## Residual risks

- A compromised Windows account can alter the program or call Windows Bluetooth controls directly.
- A compromised iPhone, Apple account, Shortcut, or Tailscale account can expose the scoped bearer token.
- Another process running as the same Windows user can access loopback and user configuration; it must still possess or replace the token hash.
- The neutral HTTPS hostname appears in public certificate-transparency logs.
- Denial of service remains possible by sleeping or shutting down the PC, signing the Windows user out, stopping Tailscale, disabling the radio through hardware/policy, or terminating the tray app.

These cases require operating-system, Apple-account, and Tailscale-account security controls outside this application.

