# Installation and development

## Current status

There are no installable administrator/agent applications or Windows services yet. Do not install this project as system-wide control on managed endpoints. The explicit proxy can be run manually after building, for local authorized testing only.

## Build prerequisites

- Windows and .NET SDK 9.0.
- Internet access to restore `Microsoft.NETFramework.ReferenceAssemblies` from NuGet on the first build.
- .NET Framework 4.8 or later installed to execute the programs.
- NuGet restore access to `System.Data.SQLite.Core` and its native x86/x64 interop assets.

Build and run policy tests from the project root:

```powershell
dotnet build .\WebBlocker.sln --configuration Release
.\scripts\test.ps1
```

The reference assemblies are build-time dependencies; the policy library targets .NET Framework 4.8. The build does not install a runtime or alter system environment settings.

Run an explicitly configured local proxy:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\WebBlocker.ProxyHost.exe --mode allowlist --rule subdomains:example.com
```

Configure a test application manually to use `127.0.0.1:18080`, then press Ctrl+C in the console to stop the proxy. The program binds to loopback only and does not alter Windows settings. It is not an authenticated service; other local processes can connect to it. Do not expose it on a LAN interface.

The default database is `%LOCALAPPDATA%\WebBlocker\proxy.db`. It holds the policy and hostname/method policy decisions, not URL paths or webpage contents. Omitting `--mode` on later launches loads the saved policy. Supplying `--mode` replaces the saved rules and increments the policy version; repeat the complete intended rule list. An empty list requires the explicit `--allow-empty-rules` switch.

Create a consistent SQLite snapshot without stopping the proxy, choosing a destination that does not already exist:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\WebBlocker.ProxyHost.exe --backup-to .\backups\proxy.db
```

To prune only access events older than a retention age, run:

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\WebBlocker.ProxyHost.exe --prune-events-older-than-days 90
```

Both operations accept `--database path` to select a non-default database. Pruning is permanent for the selected database; create and verify a backup first if the events may be needed. Retention applies only to access events and never removes the current policy, rules, or policy history.

View policy snapshots with `--policy-history`. Restore one with `--restore-policy-version <version>`; this writes the selected policy as a new version, preserving the existing history and monotonically increasing the current version. Schema v1 databases migrate transactionally to v2 and preserve the current policy as their initial history entry. Databases newer than the running application are rejected. These local history records do not identify an authenticated administrator and are not tamper-proof against the local account owner.

## Future deployment requirements

Release instructions must distinguish program files from writable configuration, database, and log locations; state privilege/runtime requirements; explain pairing and recovery; and document the signed filtering component, if any. An installer/uninstaller will not be produced until filtering behavior and its recovery path have been reviewed and tested.
