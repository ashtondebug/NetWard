# Development roadmap

This roadmap tracks the next implementation steps. “Complete” means implemented and verified at the scope stated; it does not imply production readiness or system-wide filtering.

## Completed prototype milestones

- [x] Domain policy evaluation for normalized exact and subdomain rules.
- [x] Local explicit HTTP/HTTPS CONNECT proxy and local block/timeout pages.
- [x] Local WinForms policy editor, policy history, SQLite migrations, and timed-access windows.
- [x] Persist full HTTP request URLs and show them in the local access-log view. HTTPS CONNECT remains hostname/port only.
- [x] Prune access events older than 30 days at proxy startup and hourly while running; prune at administrator startup.
- [x] Add an adult/gambling category placeholder to the administrator UI. The switches stay disabled until reviewed lists are provided.

## Next priorities

### 1. Category list import and policy enforcement

- [ ] Receive the owner-provided Markdown category files; record source, version/date, and update process.
- [ ] Define and test a strict parser with duplicate/invalid-entry reporting and a preview before import.
- [ ] Persist per-category lists and enabled state without changing the existing allowlist/blocklist behavior unexpectedly.
- [ ] Enforce the imported domains in the explicit proxy and show accurate active/inactive state in the manager.
- [ ] Test precedence, subdomains, malformed data, policy reload, and older-database migrations.
- [ ] Keep the violation notice clearly separate until actual category enforcement selects it.

### 2. Device identity and audit completeness

- [ ] Design an authorized endpoint agent/session component to obtain the real client IP and interactive Windows username; do not infer them from the loopback proxy connection or proxy process account.
- [ ] Define a measurable browsing-session duration and its limits. Do not equate request time with time spent viewing a page.
- [ ] Define HTTPS audit scope: without TLS decryption, retain hostname/port only and disclose that full HTTPS paths are unavailable.
- [ ] Restrict access to sensitive HTTP URL logs, provide appropriate notice, and verify 30-day cleanup behavior in deployed operation.

### 3. Authenticated agent/administrator communication

- [ ] Implement owner-consented pairing, device identity, authenticated transport, authorization, protocol versioning, replay/idempotency handling, and revocation.
- [ ] Synchronize policies and audit records with explicit acknowledgements and online/offline state.
- [ ] Add monitoring, retries, recovery, and security tests before enabling remote management.

### 4. Access requests and administrator notifications

- [ ] Submit requests through the authenticated agent protocol, bind each request to a device, and make retries idempotent.
- [ ] Add administrator Windows notifications and request review actions: allow with expiry, deny, or ignore/deduplicate for a configured duration.
- [ ] Audit every decision and temporary authorization; test expiry, restart, duplicate, and offline behavior.
- [ ] Keep HTTPS behavior truthful: a denied CONNECT cannot render the local request form without a separately approved TLS design.

### 5. Managed kiosk and recovery

- [ ] Prototype an explicitly launched, visible Edge kiosk session, then Chrome fallback, and document that a default-browser fallback cannot guarantee kiosk fullscreen.
- [ ] Establish administrator authentication before implementing privileged exit controls.
- [ ] Test user-consented setup, normal use, crash recovery, password recovery, safe exit, and uninstall. Never trap users or remove legitimate administrator recovery.
- [ ] Evaluate Windows 7 separately; modern Edge kiosk support cannot be assumed there.

### 6. Release readiness

- [ ] Decide and validate the actual enforcement mechanism; the current explicit proxy is bypassable and is not system-wide filtering.
- [ ] Build/test on the declared Windows 7 SP1, Windows 10, and Windows 11 support matrix.
- [ ] Add an installer, uninstaller, signed components if required, backup/upgrade handling, and documented recovery.
- [ ] Complete privacy/security review, accessibility checks, operational documentation, and end-to-end acceptance tests.

## Current release boundary

The current build is for authorized local development/testing only. It has no endpoint agent, authenticated remote management, Windows notification workflow, category URL lists, kiosk launcher, installer, or verified system-wide filtering. Do not enable category switches or claim those capabilities until their corresponding items above have been implemented and tested.
