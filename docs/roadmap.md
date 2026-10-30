# Development roadmap

This roadmap tracks the next implementation steps. “Complete” means implemented and verified at the scope stated; it does not imply production readiness or system-wide filtering.

The detailed, stable product contract is in [product requirements and implementation status](./product-requirements.md). Contributors should use that document when implementing owner-requested behavior.

## Completed prototype milestones

- [x] Domain policy evaluation for normalized exact and subdomain rules.
- [x] Local explicit HTTP/HTTPS CONNECT proxy and local block/timeout pages.
- [x] Local WinForms policy editor, policy history, SQLite migrations, and timed-access windows.
- [x] Persist full HTTP request URLs and show them in the local access-log view. HTTPS CONNECT remains hostname/port only.
- [x] Prune access events older than 30 days at proxy startup and hourly while running; prune at administrator startup.
- [x] Add an adult/gambling category placeholder to the administrator UI. The switches stay disabled until reviewed lists are provided.

## Next priorities

### 0. Define and establish the Legacy / Modern editions

- [ ] Keep Windows 7 SP1 as the minimum **target** for the Legacy edition of both Administrator and Agent; preserve the current desktop UI style and .NET Framework 4.8 floor where applicable. Do not describe this target as validated until clean Windows 7 SP1 x86/x64 testing is complete.
- [ ] Build a Modern edition of both Administrator and Agent using native WinUI 3 UI. Use the website's policy-dashboard illustration as design inspiration only; do not present it as an application screenshot or reuse demonstration data as live product state.
- [ ] Verify and select the WinUI 3 / Windows App SDK version, Modern minimum Windows build, .NET/runtime requirements, supported architectures, packaging model, prerequisites, servicing, and recovery/uninstall behavior.
- [ ] Decide repository/project boundaries for the two editions and define which domain logic and protocol contracts can safely be shared without imposing Modern runtime/API requirements on Legacy.
- [ ] Define and publish an administrator/agent compatibility matrix across editions, protocol negotiation and version errors, policy/schema compatibility, and supported upgrade/downgrade paths.
- [ ] Add independent build/test and installation validation for Legacy and Modern Administrator/Agent. Test Legacy on Windows 7 SP1 x86/x64 and the declared later Windows matrix; test Modern only on its verified minimum and declared Windows versions.
- [ ] Verify that equivalent workflows and security/accessibility behavior are consistent across both UIs; keep distinct native interaction patterns where appropriate.

### 1. Category list import and policy enforcement

- [ ] Receive the owner-provided Markdown category files (pornography/gambling and any additional owner-specified categories); record source, version/date, and update process.
- [ ] Define and test a strict parser with duplicate/invalid-entry reporting and a preview before import.
- [ ] Persist per-category lists and enabled state without changing the existing allowlist/blocklist behavior unexpectedly.
- [ ] Enforce the imported domains in the explicit proxy and show accurate active/inactive state in the manager.
- [ ] On a category match, serve the distinct violation notice for supported HTTP requests; keep HTTPS CONNECT behavior accurately documented.
- [ ] Test precedence, subdomains, malformed data, policy reload, older-database migrations, and the notice path.
- [ ] Do not use the webcam or claim camera/screen capture. Ensure the notice describes only audit evidence actually collected.

### 2. Device identity and audit completeness

- [ ] Continue recording the required audit context: device IP, full URL where observable, interactive Windows username, access time/site/method/policy outcome, and browsing duration.
- [ ] Design an authorized endpoint agent/session component to obtain the real client IP and interactive Windows username; do not infer them from the loopback proxy connection or proxy process account.
- [ ] Define a measurable browsing-session duration and its limits. Do not equate request time with time spent viewing a page.
- [ ] Define HTTPS audit scope: without TLS decryption, retain hostname/port only and disclose that full HTTPS paths are unavailable.
- [ ] Restrict access to sensitive HTTP URL logs, provide appropriate notice, and verify 30-day cleanup behavior in deployed operation.

### 3. Authenticated agent/administrator communication

- [ ] Implement owner-consented pairing, device identity, authenticated transport, authorization, protocol versioning, replay/idempotency handling, and revocation.
- [ ] Synchronize policies and audit records with explicit acknowledgements and online/offline state.
- [ ] Add monitoring, retries, recovery, and security tests before enabling remote management.

### 4. Access requests and administrator notifications

- [ ] Provide the endpoint form with the attempted URL prefilled and a reason field; submit through the authenticated agent protocol, bind each request to a device, and make retries idempotent.
- [ ] Add administrator Windows notifications that open the corresponding request in the manager.
- [ ] Provide request decisions: allow for an explicit duration, deny, or ignore for a configured duration (for example 10 minutes or 24 hours), suppressing repeat notifications for the same endpoint/target during the ignore window.
- [ ] Audit every decision and temporary authorization; test expiry, restart, duplicate, and offline behavior.
- [ ] Keep HTTPS behavior truthful: a denied CONNECT cannot render the local request form without a separately approved TLS design.

### 5. Managed kiosk and recovery

- [ ] Implement a visible, explicitly launched kiosk browsing session: Edge first, Chrome second, Windows default browser last. Document and surface that default-browser fallback cannot guarantee fullscreen.
- [ ] Meet the no-confirmation fullscreen requirement by launching the managed session before browsing; do not try to bypass browser fullscreen protections from the HTML page.
- [ ] Establish administrator authentication/password provisioning before implementing administrator-authorized kiosk exit controls.
- [ ] Test user-consented setup, normal use, crash recovery, password recovery, safe exit, and uninstall. Never trap users or remove legitimate administrator recovery.
- [ ] Match Windows-native UI conventions for Windows 7/10/11 and test each target separately; modern Edge kiosk support cannot be assumed on Windows 7.

### 6. Release readiness

- [ ] Decide and validate the actual enforcement mechanism; the current explicit proxy is bypassable and is not system-wide filtering.
- [ ] Build/test on the declared Windows 7 SP1, Windows 10, and Windows 11 support matrix.
- [ ] Add an installer, uninstaller, signed components if required, backup/upgrade handling, and documented recovery.
- [ ] Complete privacy/security review, accessibility checks, operational documentation, and end-to-end acceptance tests.

## Current release boundary

The current build is for authorized local development/testing only. It has no endpoint agent, authenticated remote management, Windows notification workflow, category URL lists, kiosk launcher, installer, or verified system-wide filtering. Do not enable category switches or claim those capabilities until their corresponding items above have been implemented and tested.
