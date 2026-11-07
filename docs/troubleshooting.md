# Troubleshooting

## Build cannot restore reference assemblies

The first build needs NuGet access to restore `Microsoft.NETFramework.ReferenceAssemblies`. Check that the configured NuGet source is reachable, then rerun the build. No dependency is installed system-wide.

## Test executable will not start

The test program targets .NET Framework 4.8. Install .NET Framework 4.8 or run it on a Windows machine with a compatible later in-place runtime. This does not imply Windows 7 has been tested. The planned Legacy edition must retain the .NET Framework 4.8 baseline for its Windows 7 SP1 target; the Modern WinUI 3 runtime baseline is not yet selected.

## Proxy cannot open the policy database

The compatibility database location is `%LOCALAPPDATA%\WebBlocker\proxy.db`. Ensure the current user can create and modify that folder, and that no external process is holding the database for exclusive access. A first-run policy replacement can be repeated after fixing the reported error.

To create a consistent backup while the proxy is running, use `--backup-to <new-path>`. The destination must not already exist. To prune old access events, use `--prune-events-older-than-days <days>` after creating any needed backup; pruning does not affect policy, rules, policy history, or timed-access windows. View saved policies with `--policy-history`; restore a snapshot using `--restore-policy-version <version>`, which creates a new version rather than overwriting the selected one. Schema v1/v2/v3/v4 databases migrate to v5 on open. A database created by a newer application schema is rejected and must be opened with a compatible application version.

## Website still loads despite a policy decision

The current proxy filters only applications explicitly configured to use `127.0.0.1:<proxy-port>`. Direct browser/app connections, alternate proxies, VPNs, DoH, and QUIC/HTTP/3 are outside its scope. It does not change Windows proxy settings or firewall rules.

If an HTTP request through the proxy is denied, the client receives the bundled block page. A denied HTTPS CONNECT cannot display that page without TLS interception; the proxy returns 403 instead. Chunked request bodies, `Expect: 100-continue`, and protocol upgrades currently receive 501.

For a timed category, the first base-allowed request each local calendar day starts the configured wall-clock countdown. Closing the browser does not pause it. Once it expires, the category stays blocked until local midnight. HTTP requests receive the timeout page; HTTPS cannot display local HTML, and an active timed HTTPS tunnel is closed at expiry.

An allowed CONNECT tunnel does not inspect encrypted traffic. A successful proxy connection is not proof of system-wide enforcement.

## LAN pairing fails

1. **Timeout / cannot connect.** Both machines must be on the same network, the endpoint window must be
   running, and inbound TCP 18770 must be allowed through Windows Firewall on the endpoint. No firewall
   rule is created automatically; approve the prompt or add an inbound rule manually. Test with
   `Test-NetConnection <endpoint-ip> -Port 18770`.
2. **Pairing code rejected.** Codes last 10 minutes and are consumed by the first successful pairing.
   After five wrong attempts the code is locked; restart the endpoint or wait for the next code cycle to
   print a fresh one.
3. **"被控端证书与已配对指纹不一致"**. The endpoint recreated its identity (for example someone deleted
   `%LOCALAPPDATA%\WebBlocker\lan\agent`). Remove the device in the administrator and pair again — an
   unexpected mismatch means the address is answering with a different device.
4. **Port already in use.** Only one endpoint instance can bind 18770. Close the other instance; the
   window shows "局域网监听未能启动" instead of silently running without a listener.

Synchronized endpoint events are stored separately from local events, keyed by device and event id, so
repeating "同步日志" never duplicates rows. The shared 30-day retention applies to them as well.

## Windows 7 behavior

Windows 7 compatibility is unverified and is a target only for the Legacy edition. The planned Modern edition has a separately determined Windows floor. Consult [compatibility.md](compatibility.md); do not infer support from a successful build on another Windows version.
