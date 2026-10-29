# Security design and current limitations

## Authorized use

Install the agent only on devices whose owner or authorized administrator has approved management. The agent must remain visible, have a clear service identity if a service is later added, and provide a legitimate authenticated recovery and uninstall path. It must not hide itself, collect page contents, credentials, cookies, or form data, or prevent an authorized owner from repairing the system.

## Current security state

The current code includes an explicit proxy that listens on `127.0.0.1` only when launched and stores policies/access decisions in a SQLite database under LocalAppData by default. It does not change system settings, store credentials, or enforce that clients use it. Local processes on the same machine can connect to it; this is not a multi-user authenticated boundary. HTTP proxy authentication headers are not forwarded to destination servers. HTTPS CONNECT is tunneled without TLS decryption.

The local database uses ordinary inherited Windows user-profile ACLs. The current schema provides transactional writes and unique event IDs, but not a tamper-evident chain or protection against the local user/account owner editing the database. Policy snapshots can be listed and restored, but the history has no authenticated actor identity and is not a trusted audit log. The events capture proxy policy decisions, not verified successful page loads. HTTP events store the complete requested URL, including path and query string; query parameters can contain sensitive data, so protect and retain the local database accordingly. HTTPS CONNECT events cannot store the encrypted path or query and record only the destination hostname and port. Access events older than 30 days are automatically pruned at proxy startup and hourly while it is running; the administrator also prunes at startup. The proxy does not currently record the client device IP address or interactive Windows username.

The initial WinForms policy manager has no login or privilege separation and uses the same local database as the proxy. It is a convenience UI for local authorized testing, not a secured administrator console. Any local account with database access can change policy; the UI cannot update policy already loaded in a running proxy. Its log view only displays proxy events and must not be interpreted as a complete record of device web activity.

Timed-access windows persist a category's first-use and expiry timestamps by local calendar date so restarting the proxy does not reset the allowance. They measure a fixed wall-clock window, not foreground-tab or active browsing time. The local user can change the system clock or database, so these windows are not tamper-resistant quotas. Enforcement applies only to explicit proxy traffic; HTTPS is not decrypted, and an expired HTTPS tunnel is closed rather than replaced with local HTML.

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

The proxy is an explicitly selected user-mode path: applications can bypass it. Do not configure endpoint proxy settings or install or activate WFP filters, drivers, firewall rules, DNS changes, or hosts-file entries as part of development without a separately reviewed plan and explicit authorization. A WFP callout must have a documented signed-driver path, compatibility tests on supported Windows 7 SP1 editions, rollback, and a recovery/uninstall procedure before deployment.

No HTTPS interception or local certificate installation is planned by default. The project must not claim to inspect HTTPS paths or show a trusted replacement page for blocked HTTPS sites without a separately authorized and reviewed TLS-interception design.
