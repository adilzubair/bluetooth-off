# Security review for v1.0.0

Review date: 2026-08-20

This review covers the source repository, all reachable Git commits, locked dependencies, release scripts, generated Windows x64 artifacts, and the live installed configuration. It is an engineering security review, not a third-party penetration test or formal certification.

## Outcome

No critical or high-severity vulnerability was identified. No plaintext credential, personal machine path, concrete tailnet DNS name, generated token, application configuration, or log file was found in the working tree or reachable Git history.

One release-level limitation remains: v1.0.0 is not Authenticode-signed. GitHub Actions provenance attestations and SHA-256 checksums establish build origin and integrity, but they do not replace Windows code signing or suppress SmartScreen warnings.

## Evidence collected

- Gitleaks 8.30.1 scanned all reachable commits and the working tree with zero findings.
- A separate metadata scan found only `example.com` test identities and a synthetic Tailscale activation URL. No unreachable Git objects were present.
- Runtime data scanning flagged the intentionally stored 64-character `tokenHash`; manual classification confirmed it was a SHA-256 hash, not the plaintext bearer token.
- Locked NuGet restore completed with auditing enabled. Current direct and transitive vulnerability and deprecation reports were empty.
- Release build completed with nullable analysis, current recommended .NET analyzers, deterministic builds, and warnings treated as errors.
- All 39 unit and integration tests passed at the review checkpoint.
- GitHub workflow YAML passed actionlint 1.7.12.
- Microsoft Defender scanning could not run because Defender is disabled on the review PC while another antivirus product is registered. This is recorded as not tested, not as a clean scan result.

## Security controls verified

### Network boundary

- The installed process had exactly one listening socket, bound to `127.0.0.1` on its configured dynamic port.
- No Bluetooth Off Windows Firewall rule existed.
- Tailscale Serve targeted the exact configured loopback URL.
- Tailscale's `AllowFunnel` flag was absent/false, confirming the route was tailnet-private.
- The Tailscale HTTPS endpoint completed trusted TLS and returned generic `401` to an unauthenticated request.

### Authentication and request handling

- Every request requires the exact HTTPS Host, exactly one `Tailscale-User-Login` identity header, and exactly one 256-bit random bearer token.
- Only SHA-256 hashes of the token and normalized Tailscale login are persisted.
- Credential hash comparisons use `CryptographicOperations.FixedTimeEquals`.
- Missing, duplicate, malformed, and incorrect credentials return the same generic `401` response.
- Credentials in URLs are ignored; state changes accept only POST and reject request bodies.
- Status and state-change routes have separate fixed-window rate limits.
- Bluetooth state changes are serialized, idempotent, and reported successful only after observing the off state.
- Kestrel request/header/body limits are bounded, its server header is disabled, CORS is not enabled, and responses are marked `no-store`.

### Information handling

- Framework request logging is disabled. Application logs accept only application categories and record correlation IDs plus exception types, not exception messages, headers, bodies, usernames, tokens, or query strings.
- Logs are bounded to seven days and 5 MiB total.
- The configuration inherited no broad write permission for Everyone, Users, Authenticated Users, or Anonymous Logon.
- The pairing token is masked by default and cleared from the Windows clipboard on pairing-window close if it still matches. Clipboard-history products can retain previous copies and remain a documented residual risk.

### Installation and supply chain

- Installation is per-user, uses a limited interactive scheduled task, and creates no firewall rule.
- Install and uninstall scripts validate absolute target paths and verify task/process ownership before replacement or removal.
- Dependencies are pinned by lock files and restored from the repository-local NuGet.org-only configuration.
- CI actions are pinned to immutable commit SHAs with minimal permissions.
- CodeQL runs on pushes, pull requests, and weekly; Dependabot covers NuGet and GitHub Actions.
- Tagged releases are rebuilt and tested on GitHub-hosted Windows runners, receive SHA-256 checksums, and are submitted for GitHub build-provenance attestation.

## Residual risks and limitations

- The Windows account, iPhone, Apple account, Tailscale account, and configured Shortcut are trusted parts of the boundary. Compromise of any of them can bypass or expose the scoped credential.
- Another process running as the same Windows user can alter the installed executable or stored hashes; that account could also control Bluetooth directly.
- An unsigned executable can be replaced by a malicious lookalike if users ignore its download source, checksum, and provenance.
- The bearer token exists in the iPhone Shortcut and may be included in Apple synchronization or backups.
- The neutral HTTPS hostname appears in public certificate-transparency records.
- The app has no automatic updater. Users must obtain future releases from the GitHub repository and verify them.
- Availability depends on the PC being awake, the user remaining signed in, Tailscale running, and Windows allowing radio control.

## Release recommendation

Publishing v1.0.0 is reasonable for personal/community use if the repository and release prominently disclose the unsigned status and provide checksums and provenance. A future broadly promoted release should add Authenticode signing from a protected CI identity and a documented key-rotation process.
