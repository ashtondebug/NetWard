# WFP feasibility assessment

## Decision

**Do not install or develop a kernel callout driver as part of the current prototype.** WFP is a viable Windows networking-filtering platform, but it does not provide a ready-made, application-independent domain policy engine. The current explicit loopback proxy remains the only implemented enforcement path.

A separate, isolated WFP evaluation may be considered after an authorized Windows test environment and a recovery plan exist. A first user-mode WFP experiment could validate application- and connection-level filtering only; it would not validate domain filtering or justify a system-wide web-filter claim.

This assessment is based on Microsoft's WFP documentation and repository/environment inspection. It is an architecture assessment, not a driver implementation, a Windows 7 compatibility result, or a security certification.

## What WFP provides

WFP is a Windows filtering platform with user-mode management APIs, a Base Filtering Engine (BFE), kernel-mode filtering layers, and optional kernel callout drivers. Basic filters can make decisions on connection metadata such as application identity, protocol, addresses, and ports. Callouts extend the platform for deeper inspection, packet or stream modification, and logging. ALE connect-redirection layers can redirect an outbound connection's remote address and port.

These are useful primitives for endpoint connection control. They do not, on their own, identify the website represented by every connection. A domain policy requires a separate, carefully designed source of hostname identity and a robust association between that identity and the connection being filtered.

## Domain-identification gaps

| Traffic or condition | What a basic WFP connection filter can establish | Additional work or limitation |
|---|---|---|
| Direct TCP connection | Application, protocol, remote address/port and other connection metadata at applicable layers | A hostname is not inherently present in the connection tuple. |
| Plain HTTP | A stream inspection component could parse an HTTP authority/`Host` value after data arrives | The request may arrive after the TCP connection is established; parsing, buffering, fragmentation, malformed requests, and connection reuse need explicit handling. |
| HTTPS over TCP | A protocol-aware stream callout might inspect a visible TLS ClientHello hostname when present | This is not HTTPS path inspection. TLS changes and ClientHello fragmentation require robust parsing; Encrypted ClientHello (ECH) can hide the name. A callout cannot infer a hidden hostname from the destination IP. |
| HTTPS over QUIC / HTTP/3 | WFP can filter the UDP connection and endpoint metadata | A basic UDP filter does not provide HTTP authority. QUIC/TLS-aware classification is a separate protocol project and is not included in this proposal. |
| DNS over HTTPS | The connection to the resolver can be filtered by application/address/port | DNS answers and requested hostnames are inside HTTPS. Blocking known resolvers is incomplete and can break legitimate services. |
| Shared CDN or hosting IP | The destination IP is visible | Multiple unrelated hostnames can share an address, so IP rules can over-block; DNS-derived mappings can become stale, be shared, or fail to cover alternate resolution paths. |
| Explicit proxy | The application connects to the proxy endpoint | The remote website name may only be available inside the proxy protocol. Blocking the proxy endpoint is not equivalent to classifying each destination site. |
| VPN or encrypted tunnel | The outer tunnel endpoint and transport may be visible | Inner destinations are hidden unless the tunnel is separately controlled and authorized. |
| IP literal or unknown destination | The address is visible | There may be no hostname to match. The product must define deterministic allow/deny behavior and report that the name is unavailable. |

DNS observation alone is not a reliable general solution: applications can use DoH, cached answers, alternate resolvers, proxies, VPNs, or shared addresses. Mapping a DNS answer to later flows also introduces cache lifetime, address reuse, multi-user, and race conditions.

## Candidate implementation paths

### A. Keep the explicit proxy

- **Benefit:** already implemented and integration-tested locally; it can match HTTP authorities and HTTPS `CONNECT` hostnames before opening an upstream connection, without TLS decryption.
- **Limit:** only applications explicitly configured to use it are filtered; direct connections and other routes bypass it.
- **Use:** continue as the safe prototype and domain-policy validation path.

### B. User-mode WFP filters for connection controls

- **Benefit:** evaluate application, protocol, and connection metadata through WFP without writing a kernel callout.
- **Limit:** does not solve general domain matching, HTTPS hostname visibility, DoH, QUIC, proxies, or VPNs. WFP API availability is not proof of correct OS-specific behavior.
- **Use:** only as an isolated feasibility experiment for connection-level policy. It must not be presented as a domain web filter.

### C. WFP callout driver plus a user-mode service

- **Potential capability:** callouts can implement deeper inspection and stream handling at selected WFP layers. A service could own policy distribution, configuration, and diagnostics while the driver applies bounded decisions.
- **Costs and risks:** native kernel code, WDK/toolchain and OS-version constraints, x86/x64 builds, signing and deployment, privileged installation, driver/service lifecycle, crash and network failure behavior, upgrade/rollback, and a tested authorized uninstall/recovery path.
- **Remaining limits:** a driver does not make HTTPS paths visible, defeat ECH, automatically classify DoH or VPN inner traffic, or turn shared IP addresses into unique domains. Supporting additional protocols would expand the attack surface and test burden.
- **Use:** not approved for implementation. Reconsider only after the decision gates below are met.

## Failure behavior and recovery requirements

Any future filtering component must make its failure mode explicit and observable:

- In allowlist mode, an unavailable or invalid policy must not silently become unrestricted access. The product must define whether traffic is blocked or the filter is disabled under an authenticated recovery action.
- In blocklist mode, it must not report a block when the filter did not enforce it.
- Service or driver communication loss, BFE/service changes, policy-update failure, resource exhaustion, and reboot/update interruption need separately tested behavior.
- A visible status and diagnostics path must distinguish policy denial from filter failure and ordinary network failure.
- Recovery and removal must be available to an authorized administrator without hidden persistence or preventing the device owner from repairing the system.
- Installation, activation, policy changes, upgrade, rollback, and removal must be tested on disposable virtual machines before any real endpoint is changed.

This repository currently has no driver, no service, no WFP filters, and no WFP health state. No system networking configuration has been changed.

## Windows 7 support assessment

Microsoft documents WFP client support beginning with Windows Vista, so Windows 7 SP1 is within the platform's stated family-level baseline. That fact does **not** establish support for this product's driver, selected WDK/WDK build configuration, signing path, APIs, dependency versions, or filtering behavior.

| Environment | Assessment |
|---|---|
| Windows 7 SP1 x86 | Platform family is within WFP's published client baseline; driver, signing, install/update, and runtime behavior are untested. |
| Windows 7 SP1 x64 | Same family-level baseline; x64 driver signing and deployment must be verified for the exact target configuration. Untested. |
| Windows 10 development host | WFP platform is available, but this machine lacks the discovered WDK/kernel build prerequisites; no WFP implementation or runtime test has been performed. |

Before claiming compatibility, pin the exact supported Windows editions, servicing prerequisites, WDK/toolchain, framework versions, driver model, signing and distribution requirements, and supported architecture. Run clean x86 and x64 Windows 7 SP1 VM tests for installation, startup, filtering, restart, update/rollback, crash behavior, recovery, and ordinary uninstall. Windows 7 is out of support; its inclusion is a product-risk decision as well as a technical test requirement.

## Decision gates before a callout prototype

All of the following are prerequisites, not tasks to bypass:

1. Define the product's actual enforcement claim and whether connection-level filtering is sufficient; otherwise specify the allowed hostname-classification sources and explicit unsupported traffic.
2. Obtain an isolated, disposable Windows test matrix for every claimed OS and architecture, plus the exact supported WDK/build/signing toolchain.
3. Write and review an authorized install, recovery, rollback, and uninstall plan. No driver should be installed on a daily-use machine during feasibility work.
4. Define fail-open/fail-closed behavior, policy integrity, health reporting, resource limits, and emergency recovery.
5. Specify and test domain identity handling for DNS changes, shared addresses, HTTP, TLS/ECH, DoH, QUIC, proxies, VPNs, and unknown destinations; document any inherent visibility limits.
6. Only then implement a minimal native experiment in a disposable VM, with tests proving both expected blocks and expected non-blocks, plus failure and recovery behavior.

Until these gates pass, continue development on the policy engine, explicit proxy, local persistence, and diagnostics without adding WFP configuration or driver code.

## References

- Microsoft Learn, [Windows Filtering Platform](https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page) — platform purpose, user/kernel APIs, and client runtime baseline.
- Microsoft Learn, [WFP Architecture](https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-architecture-overview) — filter engine, BFE, layers, and callout roles.
- Microsoft Learn, [Introduction to WFP Callout Drivers](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/introduction-to-windows-filtering-platform-callout-drivers) — deep inspection, packet/stream modification, and data logging use cases.
- Microsoft Learn, [ALE Layers](https://learn.microsoft.com/en-us/windows/win32/fwp/ale-layers) — connection authorization and connect-redirection layer behavior.
- Repository [Windows compatibility](compatibility.md) and [security design](security.md) describe the inspected development environment and current safety boundaries.
