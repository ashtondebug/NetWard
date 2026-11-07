# LAN device protocol (implemented subset)

## Status

A first, working slice is implemented: explicit pairing, mutually authenticated TLS sessions, device
status, policy push and access-event synchronization between one administrator and one endpoint on the
same LAN. It is a **local-network prototype for authorized devices**, not a remote-management platform.

Not implemented yet: protocol-version negotiation, heartbeat streams, temporary authorization,
administrator account authentication, revocation lists across devices, and Legacy/Modern interop testing.

## Transport

| Item | Value |
| --- | --- |
| Framing | TCP, TLS 1.2 (`SslStream`), one newline-delimited UTF-8 JSON message per connection |
| Endpoint | listens on `0.0.0.0:18770` (configurable constant `LanServer.DefaultPort`) |
| Administrator | connects to `endpoint-ip:18770`, one connection per operation |
| Message limit | 512 KiB per frame; longer frames are rejected |
| Concurrency | at most 16 concurrent sessions on the endpoint |
| Timeouts | 15 s TLS handshake, 60 s per request |

One request per connection is deliberate: a dropped connection can never leave half-applied state, and
there is no long-lived session to re-authenticate.

## Identity

Each device generates a self-signed RSA-2048 certificate (server and client authentication EKUs) on
first run and keeps it in `%LOCALAPPDATA%\WebBlocker\lan\<role>\identity.pfx`, together with a stable
`DeviceId` in `identity.json`. There is no certificate authority and no shared secret.

Authentication is **certificate pinning**: at pairing time the administrator stores the endpoint's
certificate thumbprint, and every later session requires an exact match in both directions. The
endpoint stores the administrator's thumbprint in `agent-state.json`.

## Pairing

1. The endpoint shows a six-digit code. The code is generated with a cryptographic RNG, valid for
   10 minutes, invalidated after one successful pairing, and locked after five wrong attempts.
2. The administrator enters endpoint IP, port and code.
3. TLS is established. The administrator accepts any endpoint certificate for this first contact and
   captures its thumbprint; the endpoint accepts any client certificate and captures its thumbprint.
4. The administrator sends `pair` with `{ Code, AdminId, AdminName }`.
5. The endpoint validates the code, stores `{ AdminId, Name, Thumbprint }`, returns
   `{ DeviceId, DeviceName, Fingerprint, Thumbprint }`.
6. The administrator aborts the pairing if the certificate used by the TLS session does not match the
   returned thumbprint and fingerprint, then persists the device record.

The six-digit code is an enrolment secret with low entropy and is never used as a long-term key. Pairing
is protected against an active relay only by comparing the short fingerprint shown on both screens;
this limitation is documented in [security.md](security.md).

## Messages

Request envelope (`LanRequest`):

| Field | Meaning |
| --- | --- |
| `RequestId` | idempotency key; repeated ids return the cached response for mutating operations |
| `Type` | `ping`, `pair`, `status`, `push-policy`, `pull-events`, `unpair` |
| `Pair` | pairing payload |
| `PolicyJson` | complete policy document, identical to the local `save-policy` input |
| `EventsQuery` | `{ SinceUtc, Limit }` |

Response envelope (`LanResponse`): `{ RequestId, Ok, Error, Message, Pair, Status, Policy, Events }`.

Error codes: `bad-request`, `unauthorized`, `invalid-code`, `code-expired`, `too-many-attempts`,
`policy-rejected`, `not-supported`, `internal`. Messages are safe to display and never contain codes,
key material or remote stack traces.

### status

Returns device identity, fingerprint, policy mode/version, rule and category counts, event count,
proxy state, uptime and the number of paired administrators.

### push-policy

The administrator sends its full policy document. The endpoint validates it with the same engine used
locally (`save-policy`), stores it transactionally and returns the new policy version. The running
proxy picks the new version up within about one second; no restart is needed.

### pull-events

Events strictly newer than `SinceUtc` are returned **oldest first**, so a client can page forward
through a backlog without skipping records. Event ids are the endpoint's stable ids; the administrator
database keeps `(DeviceId, EventId)` as its primary key, so a retried synchronization is idempotent.

### unpair

Removes the administrator from the endpoint's paired list. The administrator removes its own record
regardless of whether the endpoint answered, so revocation never depends on the other side being
reachable.

## Compatibility

Both sides currently ship the same `NetWard.Lan` assembly (targets `net48` and `net9.0`), so Legacy and
Modern builds speak the same wire format. Cross-edition pairing has **not** been tested; no protocol
version field exists yet, and adding one is the next step before any mixed deployment.
