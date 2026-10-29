# NetWard 网界卫士

NetWard 网界卫士 is a Windows application for managing domain-based web access policies on authorized devices.

## Project status

This repository is an **early local prototype**, not a production-ready endpoint-control product. It contains:

- A .NET Framework 4.8 domain-policy library with allowlist/blocklist evaluation and timed-access rules.
- A **loopback-only explicit HTTP/HTTPS CONNECT proxy**.
- Local block, timed-out, and violation-notice HTML assets.
- A versioned SQLite policy and access-event store, a local WinForms policy manager, and integration tests.
- A manual timed-access window that starts on first use each local day and continues regardless of browser closure.
- A local administrator category page listing adult and gambling categories. Its protection switches remain disabled until reviewed category lists are supplied and imported.
- Automatic 30-day access-event retention: expired events are pruned on proxy startup and hourly while it runs, and when the administrator starts.

The event store saves complete **HTTP** request URLs, including paths and query strings. Query strings may contain sensitive information. HTTPS CONNECT is not decrypted: only the destination hostname and port are visible, not the encrypted path or query. The current prototype does not record device IP addresses, interactive Windows usernames, or browsing duration.

The ordinary and timed block pages include an “申请访问” form, but it is UI-only: submissions are not sent or saved. The separate violation notice is a static design asset and is not yet selected by a category policy.

The proxy filters only applications explicitly configured to use it. It does not configure Windows networking, enforce proxy use, intercept direct traffic, run as a Windows service, pair devices, provide remote management, or implement an endpoint agent. HTTPS is not decrypted, and a denied HTTPS CONNECT cannot display a trusted local HTML page. **Do not deploy this prototype as system-wide access control.**

The target framework is .NET Framework 4.8. Although it can be installed on Windows 7 SP1, this project has not been tested on Windows 7; Windows 10/11 compatibility also requires validation on actual supported devices. Application-independent filtering, kiosk launch/exit, installer/uninstaller, and a tested recovery path remain open engineering work. No network settings, drivers, or system policies have been installed or changed.

## Owner-requested product requirements

The full requirements and current implementation status are maintained in [Product requirements and implementation status](docs/product-requirements.md). Highlights contributors should preserve:

- Allowlist/blocklist policies plus category-based, daily continuous wall-clock timed access. Expiry blocks access and uses a separate timeout page; closing the browser does not pause the chosen timer.
- “申请访问” with the attempted HTTP URL prefilled and a reason. The intended administrator workflow is a Windows notification with allow-until-expiry, deny, or time-bounded ignore/suppression for repeated requests from the same endpoint/target. This workflow is **not implemented**.
- Adult/gambling site categories based only on reviewed, owner-supplied Markdown lists. Current switches are disabled placeholders; do not invent lists or imply protection is active.
- Required audit roadmap: endpoint IP, complete URL where technically observable, interactive Windows username, browsing duration, access context, and 30-day retention. HTTPS CONNECT currently exposes hostname/port only; IP, Windows username, and browsing duration are not yet captured.
- A distinct, prominent violation notice, no webcam use, truthful evidence/audit copy, and a no-confirmation fullscreen goal implemented through a managed kiosk session. Requested browser order is Edge, Chrome, then default browser; the last fallback cannot guarantee kiosk fullscreen. Kiosk exit must be administrator-authorized with a legitimate recovery path.
- Windows-oriented management UI targeting Windows 7/10/11, with actual compatibility claims limited to versions tested on real systems.

These are product requirements, not a declaration that all features currently work. The requirement document and [roadmap](docs/roadmap.md) mark implementation gaps explicitly.

## Run the explicit proxy

The proxy changes no Windows proxy, DNS, firewall, or network settings. An administrator must explicitly configure an application to use `127.0.0.1:18080`; other traffic bypasses it. The product and executable names are NetWard, while the existing `%LOCALAPPDATA%\WebBlocker` database directory is retained for compatibility with existing user data.

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe --mode allowlist --rule subdomains:example.com
```

Use `--mode blocklist` for a blacklist, `--rule exact:example.com` for an exact match, and `--port 0` to select an available port. On the first run, supply at least one `--rule`; use `--allow-empty-rules` only when an empty rule set is intentional. Use `--help` for all options. Policy and proxy decision events are saved to `%LOCALAPPDATA%\WebBlocker\proxy.db` by default. Later runs may omit `--mode` and restore the saved policy. HTTP blocked requests receive the local `index.html` page. A denied HTTPS CONNECT receives a `403`; the browser cannot safely display the local page inside HTTPS without TLS interception, which is not performed.

The database can be backed up while the proxy is running using SQLite's online backup API:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe --backup-to .\backups\proxy.db
```

Backups do not overwrite existing files. To remove only access events older than a chosen age (for example 90 days), run `--prune-events-older-than-days 90`; policy and rule data are retained. Back up first if old events may be needed. These maintenance options operate on the selected database (`--database path` or the default LocalAppData database) and cannot be combined with proxy/policy options.

Policy changes are versioned in SQLite. View the latest history with `--policy-history`, or restore a previous version with `--restore-policy-version 3`; restore creates a new version and does not erase later history. Databases at older schema versions migrate transactionally through schema v4. Existing events remain available after migration; historical records do not gain URL data retroactively. The local database is not protected against modification by the local account owner and does not identify an authenticated administrator.

The proxy binds only to `127.0.0.1`. It does not configure the Windows or browser proxy automatically. Clients may bypass it by using direct connections, another proxy, a VPN, DNS-over-HTTPS, QUIC/HTTP/3, or other routes.

## Local policy manager

Build the solution and launch `.\src\WebBlocker.Administrator\bin\Release\net48\NetWard.Administrator.exe`. The WinForms application edits the same local policy database as the proxy: select allowlist/blocklist mode, add, edit, enable/disable, or remove normalized domain rules; configure manual timed categories and domain rules; save a new policy version; browse recent versions; and restore an earlier version as a new snapshot. The “不良网站管控” tab currently shows category placeholders only; protection remains disabled pending owner-provided lists. Timed categories apply only after the base allowlist/blocklist permits a host. Both ordinary and timed HTTP block pages include an access-request form, but submission is not connected to the administrator. HTTP requests denied after a category window expires receive `timeout.html`; HTTPS CONNECT receives 403 and an active HTTPS tunnel is closed at expiry, since TLS is not intercepted. The access-log tab displays locally stored proxy events, including full HTTP URLs, with domain search, blocked-only filtering, and pagination. HTTPS entries show the visible tunnel destination; their encrypted paths are unavailable. Use `--database <path>` to select a non-default database.

This is a local management utility only: it has no administrator login, remote-device pairing, device management, or synchronization. Logs include only events from requests routed through the explicit proxy. Saving a policy does not reload an already-running proxy; restart the proxy to use the new version. Empty rule sets prompt for confirmation because their effects differ between allowlist and blocklist modes.

## Build and test

Prerequisites:

- Windows with .NET SDK 9.0.
- NuGet access for the .NET Framework 4.8 reference assemblies and System.Data.SQLite native provider assets.
- .NET Framework 4.8 or later installed to execute the programs.

From the repository root:

```powershell
dotnet build .\NetWard.sln --configuration Release
.\scripts\test.ps1
```

## Documentation

- [Architecture and implementation status](docs/architecture.md)
- [Development roadmap and next TODOs](docs/roadmap.md)
- [Owner-requested product requirements and status](docs/product-requirements.md)
- [Contribution guide](CONTRIBUTING.md)
- [Security design and risks](docs/security.md)
- [Windows compatibility](docs/compatibility.md)
- [WFP feasibility assessment](docs/wfp-feasibility.md)
- [Communication protocol direction](docs/protocol.md)
- [Installation and development](docs/installation.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Change history](CHANGELOG.md)
- [Project instructions and acceptance criteria](AGENT.md)

## Safety and scope

Only deploy the agent to devices whose owners or authorized administrators have approved its use. The current proxy enforces rules only for requests routed through that local proxy; it must not be described as a system-wide filter until a separate enforcement mechanism has been implemented and tested against bypass paths, failure modes, and supported Windows versions. Local decision logs are not yet synchronized to an administrator or protected from modification by the local user/account owner.
