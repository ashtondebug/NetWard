# Product requirements and implementation status

This document records the product requirements requested by the project owner so contributors can implement and review the same scope. Requirements are not claims of current capability. Status labels describe this repository as it exists today.

## Product and intended use

- Product name: **NetWard 网界卫士** (English: **NetWard**).
- The intended product has two separately identifiable Windows applications: a management application and an endpoint agent. Both should be distributable as Windows executables.
- Each application is to have two editions: a **Legacy edition** for older computers and a **Modern edition** for newer supported Windows computers. This means separate legacy/modern builds of both the administrator and agent, not one edition per role.
- Legacy edition minimum target: **Windows 7 SP1**, retaining the current desktop UI style. The existing WinForms administrator is the starting point for this visual line; Windows 7 compatibility remains unverified until tested on the required x86/x64 environments.
- Modern edition: use **WinUI 3** for native Windows desktop UI and take visual direction from the website's clearly labeled policy-dashboard illustration. That illustration is only a design reference, not a software screenshot or a final UI specification. The exact minimum Windows build, Windows App SDK/runtime version, packaging model, and supported architectures must be confirmed before implementation; do not extend the Windows 7 promise to this edition.
- Both editions must represent the same product roles, policy semantics, consent/security requirements, and device identity model. Their UI and deployment stacks may differ. Protocol compatibility between legacy and modern administrator/agent builds is a separate requirement that must be explicitly specified and tested; do not assume every cross-edition pairing will work.
- Shared business rules and wire contracts should be kept independent of either UI. Reuse a binary only when its target frameworks and operating-system dependencies permit it; otherwise share source/contracts or generated protocol definitions without raising the Legacy edition's runtime/OS floor.
- Intended environments include authorized shared Windows computers in schools, government offices, and similar organizations. Deployment requires approval from the device owner or an authorized administrator, a visible program identity, clear user notice, and legitimate recovery/uninstall.
- One management installation should ultimately manage multiple authorized endpoints. Policies, identities, audit records, authorizations, and online state must be isolated per endpoint.
- Current repository status: only the local WinForms policy-manager slice and an explicitly configured loopback proxy exist. There are no Legacy/Modern release lines yet, no endpoint agent, no remote management, and no system-wide enforcement.

## Access policies and time-limited access

- Provide allowlist and blocklist modes, normalized domain rules, exact-host and subdomain matching, explicit defaults, and understandable rule precedence.
- Timed access is an additional rule category layered on top of the base allowlist/blocklist; it must not replace or silently change the base mode.
- The owner selected a **continuous wall-clock daily allowance**: the first matching request starts the category's countdown; closing the browser does not pause it; access is blocked when the allowance expires; the window resets at the endpoint's next local midnight. This is not active-tab time.
- After expiry, HTTP should be blocked and show a distinct local timeout page containing: **“本网页的可访问时间已到，已禁止访问”**. It should share the block-page design language, have no emoji, and include the same attempted-URL-prefilled “申请访问” interaction.
- HTTPS is not decrypted in the current design. A blocked CONNECT receives an HTTP proxy error, and an active timed tunnel closes at expiry; the proxy cannot replace content inside the encrypted browser page.
- Current status: base rules and daily wall-clock timed access exist for traffic routed through the explicit proxy. They do not control applications that bypass the proxy.

## Access requests and administrator decisions

### Endpoint request

- Every applicable HTTP block/timeout page has an **“申请访问”** action.
- Opening it automatically fills the attempted URL. The requester enters a reason and submits the request.
- Requests must have stable IDs, bounded/validated input, and be associated with an authenticated endpoint once device identity exists. Retries must not create duplicate approvals/notifications.
- Current status: the ordinary and timeout HTML pages have the form UI and URL prefill, but submission is not sent or persisted. An HTTPS CONNECT block cannot show the same page without a separately approved TLS design.

### Administrator workflow

- A new request should notify the administrator using a Windows notification. Selecting the notification opens the request in the management application.
- Administrator choices:
  1. **Allow** the requested site with an explicit expiry.
  2. **Deny** the request.
  3. **Ignore** the request, optionally for a configured duration (examples requested: 10 minutes or 24 hours).
- Ignore/suppression is scoped at least to the same endpoint and target, so repeated requests in the configured interval do not notify the administrator again. Expiration, restart, duplicate, offline, and re-notification behavior must be explicit and tested.
- Temporary allow decisions must expire automatically, survive restart correctly, preserve the base policy, and be audited. Permanent rule edits, temporary authorization, policy-mode changes, and disabling all protection must remain distinct actions.
- Current status: authenticated transport, notifications, decision UI, temporary grants, ignore/suppression, and actor-attributed audit are not implemented.

## Categorized-site protection and violation notice

- The management application should have an **“不良网站管控”** page with categories such as pornography and gambling. The owner will supply Markdown files containing category data. Contributors must not invent or silently substitute domain lists.
- Import should validate and preview source data, report invalid/duplicate entries, record source/version/update time, allow the administrator to choose categories, and show whether protection is actually active.
- When a reviewed/enabled category matches, the endpoint should block the HTTP request and show a notice visually distinct from normal allowlist/blocklist and timeout pages. The requested prominent Chinese copy is:
  - **“此计算机因访问违规内容已被阻断”**
  - **“请等待管理人员与你联系”**
  - The owner also requested a prominent **“已固定证据”** statement and a small notice naming the audit fields.
- The owner explicitly decided **not to use the webcam**. Do not silently capture camera or screen images. Never state that camera/screen evidence or audit fields were collected unless the corresponding implementation actually collected them. The currently bundled violation page is a static asset and is not yet selected by category policy.
- Current status: the administrator contains adult/gambling placeholders with disabled switches. No category domain lists or category enforcement are present.

## Audit fields, disclosure, and retention

The owner requires audit support for:

- Endpoint/device IP address.
- Complete requested URL.
- Interactive Windows username.
- Browsing duration.
- Access time, hostname/site, request method, policy result, and relevant rule/device identity.
- Automatic 30-day access-event retention and administrator review.

Implementation constraints contributors must preserve:

- HTTP absolute-form requests can provide a full URL; query strings can contain sensitive data and require suitable notice and access controls.
- HTTPS CONNECT reveals destination hostname and port only. Full HTTPS path/query cannot be collected without TLS decryption or client-side instrumentation. The current design does not decrypt TLS; do not claim the full HTTPS URL is known.
- The current proxy sees a loopback peer, not the endpoint's network IP. A real device address requires a designed endpoint identity/reporting mechanism and may be ambiguous behind NAT or multiple interfaces.
- The proxy process identity is not necessarily the interactive Windows user. Capture the correct Windows session identity only through an authorized, appropriately scoped endpoint component.
- A request's elapsed time is not browsing duration. Define a transparent page/session measurement with known limitations before recording that field.
- Do not collect credentials, cookies, form contents, page contents, or camera/screen images. Provide visible notice and restrict access to sensitive logs.
- Current status: HTTP full URLs are persisted and displayed; HTTPS logs expose only hostname/port. Thirty-day local event pruning runs at proxy startup and hourly and at administrator startup. Device IP, interactive username, browsing duration, remote synchronization, and authenticated administrator access are not implemented.
- The current violation page must describe actual collection, not planned requirements. A desired design mockup is not proof of data collection.

## Full-screen experience and Windows UI

- Product editions: retain the existing Windows desktop UI style for the Legacy edition (Windows 7 SP1 minimum target); build the Modern edition's native desktop UI with WinUI 3, using the website policy-dashboard illustration as visual inspiration. Both the administrator and endpoint agent need each edition. The Legacy and Modern UI implementations are not to be conflated into one runtime or treated as an OS compatibility guarantee.
- Before Modern UI implementation, verify the selected WinUI 3 / Windows App SDK version's supported Windows builds, runtime prerequisites, architecture, accessibility behavior, and packaging/deployment model. The Modern edition minimum OS remains **to be decided and verified**; Windows 7 SP1 is only the Legacy target.
- Define the manager/agent compatibility matrix across editions before release. At minimum, protocol version negotiation, unsupported-version errors, safe upgrade/downgrade behavior, and policy/data compatibility must be explicit and tested. A UI redesign must not change policy behavior or silently migrate/overwrite shared data.
- The owner requires the violation page to appear fullscreen automatically, without asking the endpoint user to confirm.
- A normal web page cannot reliably force the already-running browser into fullscreen after navigation without a browser gesture. The intended approach is to launch a visible, controlled kiosk browsing session before browsing starts; a violation page in that session is already fullscreen.
- Requested browser preference: **Microsoft Edge**, then **Google Chrome**, then the Windows default browser. The default-browser fallback may not support kiosk mode and therefore cannot guarantee fullscreen; this limitation must be visible to administrators.
- The owner requested administrator-authorized/password-protected kiosk exit. No administrator authentication/password provisioning exists yet. Do not implement a lock-in, hidden persistence, or a recovery-blocking mechanism. First provide authenticated administration, visible setup, safe exit/recovery, and uninstall.
- The Legacy management UI should use the current desktop UI style and target Windows 7 SP1 and later versions in its declared matrix. The Modern management UI should use WinUI 3 and follow the Modern edition's separately verified OS floor. Do not claim testing on a version that has not been tested on actual supported devices.
- Current status: there is no endpoint kiosk launcher, kiosk session, administrator exit authentication, tested kiosk recovery, or implemented two-edition delivery.

## Security, consent, and compatibility

- Pairing must be explicit and owner/admin-consented. Use authenticated, encrypted communication with revocation, rate limits, protocol versioning, replay/idempotency handling, and per-device isolation.
- Never hide the program, misrepresent collection, bypass OS security controls, or prevent legitimate owners/admins from recovering or uninstalling it.
- Do not add a driver, WFP filter, firewall/DNS/hosts changes, proxy auto-configuration, service installation, or startup persistence without an approved security design, explicit authorization, Windows-version assessment, and tested rollback/uninstall.
- The Legacy edition's target framework is .NET Framework 4.8; Windows 7 SP1 remains a requested compatibility target but is not validated in this development environment. The Modern edition is planned for WinUI 3; its runtime, Windows App SDK version, and minimum Windows build are not yet selected. Windows 10/11 support must be validated separately for each edition and application.
- The current explicit proxy is opt-in and bypassable. It is not system-wide access control.

## Current acceptance boundary

The repository is for authorized local development/testing. Do not mark the product complete until the relevant roadmap items have implementation, tests, documentation, and end-to-end verification on the declared Windows/browser matrix. See [the development roadmap](./roadmap.md) for prioritized work and [the security design](./security.md) for current technical boundaries.
