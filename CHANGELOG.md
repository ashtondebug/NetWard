# Change history

## Unreleased

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
