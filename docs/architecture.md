# SafeNotify architecture

## Components

```text
Windows WPF client ───────── HTTPS / SignalR ─────┐
Android client ───────────── HTTPS / REST polling ─┤
Chromebook web client ─────── HTTPS / REST polling ─┴─ Windows ASP.NET Core server
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

SQLite timestamps are stored as UTC ticks so range comparisons and sorting remain consistent with SQLite. EF Core migrations manage schema updates. When the server opens a database created by an earlier version, it checks the existing table and column layout and records the matching migration history before applying later migrations. An incomplete legacy schema stops startup instead of being silently replaced.

## Network boundaries

The server binds only to `127.0.0.1:5080` by default. A non-loopback address requires a configured TLS certificate. LAN mode adds a firewall rule limited to the local subnet and requires the certificate to be trusted by client devices. The Android app imports the public LAN root into its own secure storage and validates the server certificate and address. ChromeOS must trust the certificate through its certificate settings before the Chromebook web client can connect. Use an organisation-managed certificate and restrict firewall access to the organisation's device networks. The operator enters the server address in the client; discovery confirms the organisation name but does not grant access.

Every client uses the same join request, approval, device credential, alert delivery, and acknowledgement endpoints. The Windows client also uses SignalR. Android and Chromebook clients poll the alert API over HTTPS. Android maintains a visible foreground connection service while enabled. The Chromebook web app needs its browser window or installed app open because the local-only design does not use a remote push provider.

## Runtime files

`data/safenotify.db` contains organisation data. `data/keys` contains server data-protection keys that protect cookies and stored device credentials. The key ring is encrypted with Windows DPAPI for the server account. Both paths require restricted Windows ACLs and protected backups. Do not commit either directory.

