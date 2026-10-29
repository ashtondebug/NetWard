# Troubleshooting

## Build cannot restore reference assemblies

The first build needs NuGet access to restore `Microsoft.NETFramework.ReferenceAssemblies`. Check that the configured NuGet source is reachable, then rerun the build. No dependency is installed system-wide.

## Test executable will not start

The test program targets .NET Framework 4.8. Install the supported .NET Framework runtime for this development machine or run on a Windows machine with .NET Framework 4.8 or later. This does not imply Windows 7 has been tested.

## Proxy cannot open the policy database

The default database is `%LOCALAPPDATA%\WebBlocker\proxy.db`. Ensure the current user can create and modify that folder, and that no external process is holding the database for exclusive access. A first-run policy replacement can be repeated after fixing the reported error.

To create a consistent backup while the proxy is running, use `--backup-to <new-path>`. The destination must not already exist. To prune old access events, use `--prune-events-older-than-days <days>` after creating any needed backup; pruning does not affect policy, rules, or policy history. View saved policies with `--policy-history`; restore a snapshot using `--restore-policy-version <version>`, which creates a new version rather than overwriting the selected one. A schema v1 database migrates to v2 on open. A database created by a newer application schema is rejected and must be opened with a compatible application version.

## Website still loads despite a policy decision

The current proxy filters only applications explicitly configured to use `127.0.0.1:<proxy-port>`. Direct browser/app connections, alternate proxies, VPNs, DoH, and QUIC/HTTP/3 are outside its scope. It does not change Windows proxy settings or firewall rules.

If an HTTP request through the proxy is denied, the client receives the bundled block page. A denied HTTPS CONNECT cannot display that page without TLS interception; the proxy returns 403 instead. Chunked request bodies, `Expect: 100-continue`, and protocol upgrades currently receive 501.

An allowed CONNECT tunnel does not inspect encrypted traffic. A successful proxy connection is not proof of system-wide enforcement.

## Windows 7 behavior

Windows 7 compatibility is unverified. Consult [compatibility.md](compatibility.md); do not infer support from a successful build on Windows 10.
