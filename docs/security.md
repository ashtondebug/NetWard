# Security design and current limitations

## Authorized use

Install the agent only on devices whose owner or authorized administrator has approved management. The agent must remain visible, have a clear service identity if a service is later added, and provide a legitimate authenticated recovery and uninstall path. It must not hide itself, collect page contents, credentials, cookies, or form data, or prevent an authorized owner from repairing the system.

## Current security state

The current code includes a loopback proxy. The Modern agent sets the current user WinINET system proxy while management is enabled and restores the previous settings on pause/exit. To render the block page for denied HTTPS it installs a local root CA (`CN=NetWard Local CA`, private key in `%LOCALAPPDATA%\WebBlocker\https-ca.pfx`) and terminates TLS only for blocked hosts. Allowed HTTPS is tunneled without decryption. Local processes can connect to the proxy; this is not a multi-user authenticated boundary.

The local database uses ordinary inherited Windows user-profile ACLs. It is not tamper-evident. HTTP events store the complete requested URL. Blocked HTTPS request headers are read in memory to serve the block page, but the encrypted path/query is not stored. Allowed HTTPS is not decrypted. The proxy does not record the client device IP address or interactive Windows username.

The initial WinForms policy manager has no login or privilege separation and uses the same local database as the proxy. It is a convenience UI for local authorized testing, not a secured administrator console. Any local account with database access can change policy; the UI cannot update policy already loaded in a running proxy. Its log view only displays proxy events and must not be interpreted as a complete record of device web activity.

Timed-access windows persist a category's first-use and expiry timestamps by local calendar date. They measure a fixed wall-clock window, not foreground-tab time. Enforcement applies to proxied traffic; blocked HTTPS is TLS-terminated locally to show `timeout.html`, while allowed HTTPS tunnels are untouched.

There is currently no administrator authentication, device pairing, authenticated management transport, privileged policy protection, remote audit log, Windows service, or anti-tamper mechanism.

## Planned controls before remote management

- First-run administrator-account setup; no source-controlled default credentials.
- Password hashes using a contemporary password-hashing implementation and per-account salts; rate limits and generic, non-secret-bearing logs.
- Initial pairing code with cryptographic randomness, short expiry, attempt throttling, single-use rotation, and no plaintext logging.
- TLS with authenticated device identity and explicit certificate validation; pairing codes are not long-term keys.
- Versioned messages, unique request IDs, replay/idempotency handling, and authorization checks at the service boundary.
- Per-device policy isolation, transactional persistence, and audit records for policy, authorization, registration, and recovery changes.
- Local policy integrity checks and least-privilege file/service permissions; local administrators remain capable of recovery.
- Bounded retry/backoff, explicit health reporting, and a tested recovery mode.

These are design requirements, not implemented controls.

## Filtering-specific constraints

The proxy is a user-mode path. The Modern agent explicitly configures the current user system proxy and a local CA; applications can still bypass it (direct sockets, other proxies, VPN, DoH, QUIC). Do not add WFP drivers, firewall rules, DNS/hosts changes, or machine-wide policies without a separately reviewed plan and explicit authorization.

HTTPS interception is deliberately limited to hosts the policy already blocks. The local CA private key can sign any certificate and must be treated as a high-value secret: it is stored unprotected in the current user profile, so any process running as that user can read it. Use only on authorized test devices; remove the CA and pfx when finished.
