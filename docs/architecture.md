# SafeNotify architecture

## Components

```text
Windows WPF client ── HTTPS / SignalR ── ASP.NET Core server
                                              ├── Admin dashboard
                                              ├── Device registration API
                                              ├── Alert and acknowledgement API
                                              └── SQLite database
```

The server hosts the static dashboard, REST endpoints, the SignalR device hub, and an expiry worker. The browser administrator interface authenticates with a secure cookie. The Windows client uses an installation ID and a random claim token to request membership. The server stores only a hash of the claim token. After approval it stores a protected copy of a random device credential and its hash; possession of the claim token allows the client to retrieve that credential after reconnecting.

Admin routes require authentication and unsafe admin requests require an antiforgery token. Device routes require a bearer credential whose hash matches an active approved device. The SignalR hub validates the same credential during connection. Revocation replaces the server-side credential hash and removes the protected credential, so a saved token cannot authenticate another request or reconnect.

Alerts have one delivery row per recipient device. The server records a connection as sent, while the client reports delivered after it receives and presents the alert. Acknowledgement is separate from delivery. Emergency resolution is an administrator action and is recorded separately from recipient acknowledgement. Unexpired offline alerts are retrieved when a client reconnects; expired alerts are omitted and marked expired by the server worker.

## Data model

- `Organisation`: local organisation name.
- `Administrator`: email address and ASP.NET Core password hash.
- `Device`: installation identity, credential hash, protected credential, approval state, last-seen time, and optional group.
- `JoinRequest`: device name, claim-token hash, decision, and administrator identity.
- `DeviceGroup`: audience grouping.
- `Alert`: message, priority, audience, creator, and expiry time.
- `Delivery`: per-device delivery, acknowledgement, and resolution state.
- `AuditEntry`: administrative and device actions.

SQLite timestamps are stored as UTC ticks so range comparisons and sorting remain consistent with SQLite. The current database uses EF Core `EnsureCreated`; schema migration scripts are a release requirement before changing a deployed schema.

## Network boundaries

The server binds only to `127.0.0.1:5080` by default. A non-loopback address requires a configured TLS certificate. Use an organisation-managed certificate and restrict firewall access to the organisation's device networks. Discovery is intentionally not an authorization mechanism. In this build, the operator enters the server address manually.

## Runtime files

`data/safenotify.db` contains organisation data. `data/keys` contains server data-protection keys that protect cookies and stored device credentials. The key ring is encrypted with Windows DPAPI for the server account. Both paths require restricted Windows ACLs and protected backups. Do not commit either directory.

