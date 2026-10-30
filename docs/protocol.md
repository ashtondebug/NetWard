# Communication protocol direction

## Status

No network listener, pairing flow, or remote-management protocol is implemented yet.

## Proposed first version

Use an authenticated TLS transport over TCP with a versioned, length-bounded message envelope. Verify TLS, certificate validation, and deployment behavior separately for the Legacy Windows 7 SP1/.NET Framework 4.8 target and the Modern edition's selected runtime/Windows App SDK baseline. Before choosing an HTTP API or raw TLS framing, test the actual deployment environments. Do not invent cryptography or treat a pairing code as a permanent secret.

The protocol must not be coupled to either UI framework. Define explicit protocol-version negotiation and an administrator/agent compatibility matrix before promising Legacy-to-Modern interoperability. Unsupported pairings must fail clearly and safely; do not silently downgrade authentication or policy behavior.

Every request should carry:

- Protocol version.
- Message type.
- Unique request ID.
- Stable device ID when assigned.
- Authenticated peer identity.
- Bounded payload and explicit validation rules.

Initial message families: pairing, device hello/status, heartbeat, policy fetch/push and acknowledgement, access-event upload and acknowledgement, temporary authorization/revocation, diagnostics, and protocol-version negotiation.

Pairing should use a short-lived high-entropy code, throttled attempts, one-time consumption/rotation, authenticated device identity establishment, and explicit owner/admin consent. Subsequent communication must use a distinct long-term credential with revocation support. Sensitive material must never be logged.

## Reliability and isolation

Use idempotent request IDs for mutating operations, stable event IDs for log deduplication, explicit acknowledgements, bounded exponential backoff, and per-device policy/event keys. Reconnect must fetch the latest desired policy and report the applied policy version; unsent changes must not be displayed as applied.

Message size limits, timeout behavior, duplicate/out-of-order handling, version incompatibility, and error codes remain to be implemented and tested.
