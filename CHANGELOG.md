# Change history

## Unreleased

- Initialized the Windows 7-aware project structure and documented the current architecture and filtering limitations.
- Added a .NET Framework 4.8 domain-policy engine with IDN normalization, strict hostname validation, exact/subdomain matching, duplicate detection, and whitelist/blacklist evaluation.
- Added a loopback-only, explicitly configured HTTP/HTTPS CONNECT proxy and a self-contained offline HTTP block page.
- Added a versioned SQLite schema for atomic policy persistence and durable, deduplicated proxy decision events, with blocked-only/domain search and bounded paging.
- Added integration tests for policy restart recovery, event deduplication/filtering, HTTP forwarding and denial, HTTPS CONNECT denial, allowed raw TCP tunneling, block-page failure reporting, and mismatched Host rejection.
- Added a WFP feasibility assessment covering domain-identification limits, candidate implementation paths, Windows 7 validation gates, failure handling, and the decision not to add a driver to the current prototype.
- Added SQLite online backup and age-based access-event pruning maintenance commands.
- Added schema v2 policy snapshots, v1-to-v2 migration, bounded local history listing, and rollback as a new policy version; migration and unsupported future-version behavior are covered by tests.
- The proxy is bypassable and does not configure Windows networking, decrypt TLS, inspect HTTPS paths, or provide endpoint-wide filtering. Device communication, GUIs, service, and driver are not implemented yet.
