# Installation and development

## Current status

There are no installable administrator/agent applications or Windows services yet. Do not install this project as system-wide control on managed endpoints. The explicit proxy can be run manually after building, for local authorized testing only.

## Build prerequisites

- Windows and .NET SDK 9.0.
- Internet access to restore `Microsoft.NETFramework.ReferenceAssemblies` from NuGet on the first build.
- .NET Framework 4.8 installed to execute the current prototype. For the planned Legacy edition's Windows 7 SP1 target, retain .NET Framework 4.8; do not assume .NET Framework 4.8.1 supports Windows 7.
- NuGet restore access to `System.Data.SQLite.Core` and its native x86/x64 interop assets.

Build and run policy tests from the project root:

```powershell
dotnet build .\NetWard.sln --configuration Release
.\scripts\test.ps1
```

The reference assemblies are build-time dependencies; the current policy library targets .NET Framework 4.8. The build does not install a runtime or alter system environment settings. The planned Modern edition uses WinUI 3 and does not yet have a selected Windows App SDK/runtime or OS baseline.

## Local policy manager

After building the solution, run:

```powershell
.\src\WebBlocker.Administrator\bin\Release\net48\NetWard.Administrator.exe
```

The WinForms application reads and writes the local `%LOCALAPPDATA%\WebBlocker\proxy.db` database. It can edit the allowlist/blocklist mode and domain rules, configure manual timed categories and domain rules, list the latest 100 policy snapshots, restore a historical version as a new version, and browse locally persisted proxy events with domain search, blocked-only filtering, and pagination. Timed categories overlay the base policy: only base-allowed requests are timed. The first matching request each local calendar day starts a continuous wall-clock countdown that does not pause when the browser closes; expiry blocks that category until local midnight. Pass `--database <path>` to select another database. It is not authenticated and does not manage remote devices. Logs include only requests handled by the explicit proxy. A running proxy must be restarted to load a policy saved by this application.

Run an explicitly configured local proxy:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe --mode allowlist --rule subdomains:example.com
```

Configure a test application manually to use `127.0.0.1:18080`, then press Ctrl+C in the console to stop the proxy. The program binds to loopback only and does not alter Windows settings. It is not an authenticated service; other local processes can connect to it. Do not expose it on a LAN interface.

The legacy default database location remains `%LOCALAPPDATA%\WebBlocker\proxy.db` so existing policy data is preserved after the product rename. It holds policy and proxy access decisions. HTTP events include the complete requested URL, including path and query; query strings may contain sensitive information. HTTPS CONNECT events include the visible destination hostname and port only because TLS is not decrypted. Historical events migrated from older schemas have no retroactively populated URL. Omitting `--mode` on later launches loads the saved policy. Supplying `--mode` replaces the saved rules and increments the policy version; repeat the complete intended rule list. An empty list requires the explicit `--allow-empty-rules` switch.

Create a consistent SQLite snapshot without stopping the proxy, choosing a destination that does not already exist:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe --backup-to .\backups\proxy.db
```

To prune only access events older than a retention age, run:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe --prune-events-older-than-days 90
```

Both operations accept `--database path` to select a non-default database. Pruning is permanent for the selected database; create and verify a backup first if the events may be needed. Retention applies only to access events and never removes the current policy, rules, or policy history. The proxy automatically deletes events older than 30 days at startup and hourly while running; the administrator also prunes at startup. Verify backups before applying manual pruning if older records may be needed.

View policy snapshots with `--policy-history`. Restore one with `--restore-policy-version <version>`; this writes the selected policy as a new version, preserving the existing history and monotonically increasing the current version. Older databases migrate transactionally through schema v4 and preserve existing policy history. Databases newer than the running application are rejected. These local history records do not identify an authenticated administrator and are not tamper-proof against the local account owner.

Timed access is enforced only on requests routed through the explicit proxy. Expired HTTP requests receive `timeout.html`. HTTPS CONNECT is not decrypted, so it receives 403 on a new denied connection; an already-open timed HTTPS tunnel is closed at expiry and cannot show a local HTML page.

## Future deployment requirements

Release instructions must distinguish program files from writable configuration, database, and log locations; state privilege/runtime requirements; explain pairing and recovery; and document the signed filtering component, if any. An installer/uninstaller will not be produced until filtering behavior and its recovery path have been reviewed and tested.
