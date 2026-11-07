# Change history

## Unreleased

### 局域网跨设备管理（Alpha）

- Added `src/NetWard.Lan` (targets `net48` and `net9.0`): per-device self-signed identity, pairing state,
  a mutually authenticated TLS 1.2 listener/client and a bounded one-request-per-connection JSON frame.
- The Modern endpoint now listens on `0.0.0.0:18770`, shows a real six-digit pairing code (10-minute
  lifetime, single use, locked after five failed attempts) and reports connection state from actual
  authenticated sessions instead of the local heartbeat file.
- The Modern administrator gained a 设备连接 page: pair by IP/port/code, live status, push the current
  policy, synchronize access events and revoke a pairing. The device list is stored in
  `%LOCALAPPDATA%\WebBlocker\lan\admin\devices.json`.
- Trust uses certificate pinning in both directions; a changed endpoint certificate is rejected. The short
  fingerprint is shown on both screens so an operator can detect a relay during first pairing — this is a
  manual check and is documented as a limitation.
- Policy push reuses the engine bridge, so remote writes get the same validation and transaction as local
  saves, and the running proxy hot-reloads the new version.
- SQLite schema v6 adds `RemoteAccessEvents` keyed by `(DeviceId, EventId)`. Synchronization is idempotent
  per event, devices are isolated from each other, and the 30-day retention now prunes remote events too.
- Fixed a silent policy-loss defect: `policy-json` omitted timed-access rules and blocked-content rules, so
  any save from the Modern administrator rewrote the policy without them. Both collections are now
  exported, and the administrator parses them back.
- `events-json` now includes the stable event id and supports `--since` / `--ascending`; new bridge
  commands `import-remote-events` and `remote-events-json` back the synchronization.
- Test suite grew from 30 to 41 tests: real loopback TLS pairing, wrong/expired/locked codes, rejection of
  an unpaired administrator, detection of a changed endpoint certificate, per-device event isolation and
  idempotent import, and bridge contract tests that run the real `NetWard.EngineBridge.exe`.

### Alpha：可运行的受管会话版本

- Added `NetWard.Agent`, a Legacy/WinForms endpoint console that loads the saved policy, starts the
  loopback proxy, shows the policy and category summary, lists access events with violations highlighted
  in red, and launches a managed browsing session.
- Added the managed browsing session: a dedicated browser profile plus `--proxy-server` forces Edge or
  Chrome through the local proxy without changing Windows proxy settings. Internet Explorer, when used,
  applies and then restores the per-user WinINET proxy values.
- Renamed the ordinary denial page from `index.html` to `block.html` and added a `BlockPageCatalog` so the
  proxy routes each decision to the correct bundled page: `block.html`, `timeout.html` or `violation.html`.
- Added blocked-content category enforcement: `CategoryConfig`/`CategoryRule`, category evaluation that
  runs before the base allowlist/blocklist decision, and the distinct violation page on a match.
- Added SQLite schema v5 with `BlockedCategories`, `BlockedCategoryRules` and their policy-history tables,
  the `BlockedByCategory` access result, and the v4-to-v5 migration.
- Added Markdown category list import in the administrator with a preview of parsed categories, domain
  counts, duplicates and rejected lines, and an enable/disable switch per category.
- Added `samples/category-list.example.md`, which only contains reserved test domains, so the import and
  violation path can be demonstrated without inventing real category data.
- Fixed a long-standing inverted blocklist evaluation: a matching rule in blocklist mode now denies access,
  and every non-matching host is allowed.
- Fixed overlapping-rule evaluation: the most specific rule wins, independent of storage order, so audit
  records name the rule that actually decided the request.
- Fixed unhandled `InvalidDataException` crashes: a corrupted or newer-schema database now reports a readable
  message in both the proxy host and the administrator instead of terminating with an unhandled exception.
- Added startup validation for bundled denial pages: a missing page stops the host with a clear message and
  the 503 fallback is still recorded as `BlockPageFailure`.
- Extended the automated suite to 30 tests, including most-specific rule matching, category evaluation,
  page routing, category persistence and an end-to-end violation-page proxy test.

- Reworked the Modern UI around native WinUI 3: `NavigationView` sidebar, `MicaBackdrop`, system theme brushes, `InfoBar` status, `AutoSuggestBox` search, `ToggleSwitch` theme control and `AccentButtonStyle` primary actions. Removed the custom palette and self-drawn navigation buttons.
- Fixed a runtime XAML failure by merging `XamlControlsResources` into `Application.Resources`.

- Added a hidden Easter egg to `block.html` and `timeout.html`: ten quick clicks on the shield logo open a menu that runs the terminal-style `world.execute(me);` experience inside the block page via an inline `iframe srcdoc`, with CSS, JavaScript, fonts and audio embedded as data URIs (no external requests).
- Re-encoded the experience audio to 32 kbps mono to keep each self-contained page around 1.8 MB; added an IE-compatible decoding/`document.write` fallback.

- Proxy now hot-reloads the saved policy within one second of an administrator save; the agent no longer needs a restart and the listening port stays stable.
- The agent refreshes its displayed policy version after the running proxy reports a change.

- Blocked HTTPS requests now show the normal NetWard block page: the agent installs a local root CA, terminates TLS only for denied hosts, and leaves allowed HTTPS tunnels untouched.
- The agent configures the current-user WinINET system proxy while management is enabled and restores the previous settings on pause/exit.
- Added URL-shaped rule normalization so `https://example.com/path` is stored as `example.com`; host matching covers both HTTP and HTTPS.

### Modern Alpha（WinUI 3）与引擎桥

- Added `src/NetWard.EngineBridge`, a net48 console bridge exposing the existing policy/event database as JSON (`status-json`, `policy-json`, `events-json`) and hosting the proxy for a parent process (`run --parent-pid`), so the Modern front-end reuses the verified engine without a second data format.
- Added `src/NetWard.Modern`, a WinUI 3 (net9.0-windows) front-end with real policy, category and event views, protection start/stop, managed browsing, and a link to the Legacy administrator. Visual language follows `demos/modern`; the demo projects remain static design references.
- The Modern app copies the engine and the Legacy administrator into its output (`engine\`, `tools\legacy-admin\`) so it runs as a self-contained folder.

## Unreleased（此前内容）
- Reorganized the README around the project overview, capability/status matrix, quick start, component map, architecture/privacy boundaries, documentation index, and contribution entry point; added a complete docs index and aligned the public requirements summary.
- Renamed the product and Visual Studio solution to NetWard 网界卫士; updated app metadata, executable names, window/CLI titles, and HTML branding while keeping legacy code namespaces and local data paths stable.
- Initialized the Windows 7-aware project structure and documented the current architecture and filtering limitations.
- Added a .NET Framework 4.8 domain-policy engine with IDN normalization, strict hostname validation, exact/subdomain matching, duplicate detection, and whitelist/blacklist evaluation.
- Added a loopback-only, explicitly configured HTTP/HTTPS CONNECT proxy and a self-contained offline HTTP block page.
- Added a versioned SQLite schema for atomic policy persistence and durable, deduplicated proxy decision events, with blocked-only/domain search and bounded paging.
- Added integration tests for policy restart recovery, event deduplication/filtering, HTTP forwarding and denial, HTTPS CONNECT denial, allowed raw TCP tunneling, block-page failure reporting, and mismatched Host rejection.
- Added a WFP feasibility assessment covering domain-identification limits, candidate implementation paths, Windows 7 validation gates, failure handling, and the decision not to add a driver to the current prototype.
- Added SQLite online backup and age-based access-event pruning maintenance commands.
- Added schema v2 policy snapshots, v1-to-v2 migration, bounded local history listing, and rollback as a new policy version; migration and unsupported future-version behavior are covered by tests.
- Added a WinForms local management utility for mode/rule CRUD, policy history rollback, and real SQLite access-event browsing with search, blocked-only filtering, and pagination; it remains unauthenticated and local-only.
- Added manual timed-access categories as a local overlay on allowlist/blocklist policy, persistent per-category daily wall-clock windows, timeout-page enforcement, and schema v3 migration/history support.
- Added timeout-page consistency, daily-window, migration, persistence, and proxy-denial tests.
- Restyled the block and timeout pages to match the supplied responsive light/dark design; added an attempted-URL-prefilled access request form UI while clearly marking that submission is not yet connected.
- Added the same access-request form and attempted-URL prefill behavior to the timed-access timeout page.
- Added a standalone Windows-blue violation notice HTML asset; it is bundled but not yet wired to category-based policy decisions and now describes the fields actually available in local proxy logs.
- Added schema v4 storage for complete HTTP request URLs and destination ports, with transactional migration from earlier schemas; HTTPS CONNECT logs retain the visible hostname and port only.
- Added requested-URL display in the administrator's access-log view and explicit disclosure that IP address, interactive Windows username, and browsing duration are not yet recorded.
- Added automatic 30-day access-event retention at proxy and administrator startup, plus hourly cleanup while the proxy is running.
- Added a local administrator “不良网站管控” placeholder tab for the explicitly requested adult and gambling categories; protection switches remain disabled until the administrator-provided category lists are received and imported.
- Added a prioritized development roadmap and contributor guidance, and updated the README/installation guide to reflect schema v4, complete HTTP URL logging, and automatic 30-day event retention.
- The proxy is bypassable and does not configure Windows networking, decrypt TLS, inspect HTTPS paths, or provide endpoint-wide filtering. Remote device communication, Windows notifications/access requests, service, and driver are not implemented yet.
