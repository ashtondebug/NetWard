# WebBlocker

WebBlocker（网页访问安全管家） is a Windows administrator/agent system for managing domain-based web access policies on authorized devices.

## Project status

This repository is at an early prototype stage. It contains a reusable domain-policy engine, a runnable **loopback-only explicit HTTP proxy**, a local blocked page, an SQLite policy/event store, and executable integration tests. It does **not** enforce proxy use, intercept direct network traffic, inspect/decrypt HTTPS, run a Windows service, pair devices, or provide administrator/agent GUIs. Do not deploy it as system-wide access control.

The proposed desktop target is .NET Framework 4.8 with WinForms, which Microsoft lists as installable on Windows 7 SP1 (32-bit and 64-bit). The current development machine is Windows 10 64-bit; Windows 7 has not been tested. Reliable, application-independent domain enforcement remains an open engineering task requiring a separately assessed WFP callout driver. No network settings or drivers have been installed or changed.

## Run the explicit proxy

The proxy changes no Windows proxy, DNS, firewall, or network settings. An administrator must explicitly configure an application to use `127.0.0.1:18080`; other traffic bypasses it.

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\WebBlocker.ProxyHost.exe --mode allowlist --rule subdomains:example.com
```

Use `--mode blocklist` for a blacklist, `--rule exact:example.com` for an exact match, and `--port 0` to select an available port. On the first run, supply at least one `--rule`; use `--allow-empty-rules` only when an empty rule set is intentional. Use `--help` for all options. Policy and proxy decision events are saved to `%LOCALAPPDATA%\WebBlocker\proxy.db` by default. Later runs may omit `--mode` and restore the saved policy. HTTP blocked requests receive the local `index.html` page. A denied HTTPS CONNECT receives a `403`; the browser cannot safely display the local page inside HTTPS without TLS interception, which is not performed.

The database can be backed up while the proxy is running using SQLite's online backup API:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\WebBlocker.ProxyHost.exe --backup-to .\backups\proxy.db
```

Backups do not overwrite existing files. To remove only access events older than a chosen age (for example 90 days), run `--prune-events-older-than-days 90`; policy and rule data are retained. Back up first if old events may be needed. These maintenance options operate on the selected database (`--database path` or the default LocalAppData database) and cannot be combined with proxy/policy options.

Policy changes are versioned in SQLite. View the latest history with `--policy-history`, or restore a previous version with `--restore-policy-version 3`; restore creates a new version and does not erase later history. The v1 database schema migrates to v2 and carries its current policy into the history. This local history is not protected against modification by the local account owner and does not identify an authenticated administrator.

The proxy binds only to `127.0.0.1`. It does not configure the Windows or browser proxy automatically. Clients may bypass it by using direct connections, another proxy, a VPN, DNS-over-HTTPS, QUIC/HTTP/3, or other routes.

## Build and test

Prerequisites:

- Windows with .NET SDK 9.0.
- NuGet access for the .NET Framework 4.8 reference assemblies and System.Data.SQLite native provider assets.
- .NET Framework 4.8 or later installed to execute the programs.

From the repository root:

```powershell
dotnet build .\WebBlocker.sln --configuration Release
.\scripts\test.ps1
```

## Documentation

- [Architecture and implementation status](docs/architecture.md)
- [Security design and risks](docs/security.md)
- [Windows compatibility](docs/compatibility.md)
- [WFP feasibility assessment](docs/wfp-feasibility.md)
- [Communication protocol direction](docs/protocol.md)
- [Installation and development](docs/installation.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Change history](CHANGELOG.md)

## Safety and scope

Only deploy the agent to devices whose owners or authorized administrators have approved its use. The current proxy enforces rules only for requests routed through that local proxy; it must not be described as a system-wide filter until a separate enforcement mechanism has been implemented and tested against bypass paths, failure modes, and supported Windows versions. Local decision logs are not yet synchronized to an administrator or protected from modification by the local user/account owner.
