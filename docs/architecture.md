# Architecture and implementation status

## Current decisions

| Area | Decision | Status |
|---|---|---|
| Administrator and agent desktop apps | C# / .NET Framework 4.8 / WinForms | Selected for the Windows 7 SP1 baseline; GUIs not started |
| Shared business logic | .NET Framework 4.8 class library | Started: domain normalization and policy evaluation |
| Network filtering | Loopback-only, explicitly configured HTTP proxy; WFP driver deferred pending feasibility gates | Proxy prototype implemented; assessment documented; no driver installed |
| Device communication | Versioned, authenticated TLS protocol | Not implemented |
| Persistence | SQLite with versioned schema, transactional policy updates, policy history/rollback, online backup, and event retention | Schema v2 and store implemented; local CLI available; no management UI or remote synchronization |

Microsoft documents .NET Framework 4.8 as installable on Windows 7 SP1, both 32-bit and 64-bit. This does not mean that current hardware, TLS defaults, updates, or a future driver have been validated on Windows 7. See [Windows compatibility](compatibility.md).

## Filtering assessment

A user-mode proxy can inspect HTTP `Host` and TLS `CONNECT` destinations, but it does not by itself force every browser or application to use that proxy. Direct sockets, alternate proxies, VPNs, and encrypted DNS can bypass or hide destinations. Changing proxy settings or `hosts` is not a system-wide enforcement design.

WFP is available starting with Windows Vista and exposes user-mode and kernel-mode APIs. It provides packet/socket filtering primitives, not a ready-made hostname firewall. Basic connection filters can act on application and connection metadata, while domain decisions require separate hostname identification and association with the flow. DNS-to-IP state, HTTP hostnames, TLS SNI/ECH, encrypted DNS, address sharing, QUIC/HTTP/3, proxies, VPNs, and failure recovery all need explicit treatment. A kernel callout adds driver signing, update, crash, and uninstall risks; it must not be introduced until the Windows 7 support matrix and recovery path are tested. See the [WFP feasibility assessment](wfp-feasibility.md) for the decision, trade-offs, limitations, and decision gates.

The first enforcement prototype is a visible console process that listens only on `127.0.0.1`. It filters only requests from applications explicitly configured to use that proxy:

- HTTP absolute-form requests are matched before a connection to the destination is made. Allowed requests are forwarded as one request per connection, with hop-by-hop/proxy authentication headers removed and `Connection: close` enforced.
- HTTPS `CONNECT` authorities are matched before a TCP tunnel is opened. An allowed connection is tunneled without decrypting TLS.
- Denied HTTP requests receive the bundled offline `index.html`; denied HTTPS CONNECT requests receive HTTP 403 to the proxy client and are not redirected to a local page.
- Malformed authorities and request/Host mismatches are rejected. Header size is limited to 32 KiB, body size to 16 MiB, and concurrent clients to 32. Chunked request bodies, `Expect`, and protocol upgrades are unsupported.
- The proxy does not change Windows settings, start automatically, enforce that applications use it, or report traffic from applications that bypass it. It is not a service, endpoint firewall, or system-wide filter.
- Policy and hostname-level proxy decisions are persisted in SQLite under the current user's local application-data directory. Policy changes and their monotonically increasing version are committed in one transaction. Each event has a stable unique ID; duplicate inserts are ignored. Queries support a blocked-only filter, case-insensitive domain substring search, newest-first ordering, and bounded pagination.
- Events deliberately contain a domain and HTTP method, not URL paths, query strings, usernames, page content, or credentials. `AllowedByPolicy` means the proxy decision allowed a request; it does not assert that the destination successfully served a page. TLS CONNECT entries contain only the hostname, not the encrypted path.
- SQLite schema version 2 includes current policy/rules, an append-only-by-application policy snapshot history, and access events. Policy saves and their history snapshots are committed atomically. `--policy-history` lists up to the latest 100 snapshots; `--restore-policy-version N` reapplies a previous snapshot as a new monotonically increasing policy version rather than rewriting the past. A v1 database is migrated transactionally and its current policy is retained as the initial history entry. Database connections disable pooling, use SQLite's full synchronous setting, and wait up to five seconds for a busy database. A write failure prevents an allowed request from reaching its upstream destination; the client receives an error instead. The history contains no authenticated actor identity and is not tamper-evident: local application-data ACLs are relied upon, and the local user/account owner can modify the database.
- Schema version 0 (a new SQLite database) is initialized transactionally through version 1 to version 2. The store rejects databases with a newer version instead of silently opening or rewriting them. The `--backup-to` CLI maintenance action uses SQLite's online backup API to create a consistent snapshot without overwriting an existing destination. `--prune-events-older-than-days` deletes only access events strictly older than its UTC cutoff; policy/rules/history are not deleted. Back up before pruning when the records may be needed.
- The default first-run policy must be explicit. Replacing a policy with an empty rule set requires `--allow-empty-rules`. Re-running with `--mode` replaces the saved policy and increments its version; omitting it restores the prior policy. The access-log query's `onlyBlocked` flag filters denied events only when true; false returns all events (subject to any domain search and pagination).

The integration tests use only local loopback endpoints; they exercise actual HTTP forwarding, policy denial before upstream connect, local block-page responses, HTTPS CONNECT denial, and allowed TCP tunnel transfer. They do not establish browser compatibility, internet connectivity, TLS inspection, or Windows 7 compatibility.

This prototype does not claim that a decision from the policy library blocks direct packets. Until a system-level enforcement component is approved and validated:

- Direct HTTP and HTTPS requests are not intercepted; only explicitly proxied requests are filtered.
- HTTPS paths are not inspected or decrypted.
- DNS-over-HTTPS, QUIC, explicit proxies, and VPNs are not controlled.
- A local `index.html` cannot safely replace an HTTPS page without a browser-trusted interception mechanism; no TLS interception is planned by default.
- No filtering failure mode or enforcement health state exists yet.

## Policy semantics

The shared engine accepts normalized hostnames only:

- Whitelist mode: a matching enabled rule allows the host; otherwise it is denied.
- Blacklist mode: a matching enabled rule denies the host; otherwise it is allowed.
- Exact rules match only the configured hostname.
- Subdomain rules match the configured hostname and descendants at a label boundary.
- Scheme, port, path, IP literals, malformed names, and ambiguous inputs are not domain rules.
- Disabled rules do not match.
- Identical normalized domain/scope pairs are rejected as duplicates.
- Temporary authorization precedence is not implemented. Future policy evaluation must define temporary grants as narrow, device-scoped, expiring exceptions without mutating the policy mode.

The enforcement adapter must separately define how IP literals and unclassifiable destinations are treated. It must fail closed in whitelist mode, surface an explicit diagnostic, and never report a successful block when the filter is unhealthy.

## Planned components

1. `WebBlocker.Shared`: policy and protocol models usable by both desktop apps.
2. `WebBlocker.Agent`: visible agent UI and, only after review, a clearly named Windows service.
3. `WebBlocker.Administrator`: local administrator UI, authentication, and device management.
4. Filtering component: separate, signed and recoverable component only after a Windows 7 feasibility review and an authorized test plan.
5. Persistence expansion: configurable retention scheduling, authenticated per-device policy administration, and actor-attributed audit events.
6. Tests: policy and storage unit tests first; then protocol, authorized integration, and OS/browser compatibility tests.

## Initial data model direction

The implemented local schema includes singleton policy settings, rules, and proxy access events. The initial schema creation is transactional and newer unsupported schema versions are rejected. The proxy host offers explicit online backup and age-based event-pruning maintenance actions. The planned management schema additionally includes devices, temporary authorizations, administrator accounts, and audit events. Device-specific policy and event records must be keyed by stable device IDs; event IDs must be unique for idempotent synchronization. Future schema changes require sequential, transactional migrations and explicit tests.

## Known risks

- Domain-to-IP mapping can over-block shared hosting/CDNs or become stale.
- TLS Encrypted ClientHello can hide SNI; inspecting TLS payload is not equivalent to reading HTTPS paths.
- DoH, QUIC, proxies, and VPNs require explicit handling; default browser behavior is not a security boundary.
- Windows 7 is out of support, and 32-bit and 64-bit driver/signing paths differ.
- Fail-closed filtering can interrupt legitimate connectivity; a documented, authenticated recovery route is mandatory.
- This host has no discovered WDK/kernel headers or Windows 7 test VM.
