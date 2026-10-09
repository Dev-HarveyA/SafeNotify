# Security policy

SafeNotify is under active development and has not received an independent security assessment. Do not use it as the only emergency notification or life-safety system.

## Reporting a vulnerability

Do not post exploitable vulnerability details in a public issue. Use GitHub's private vulnerability reporting for the repository when it is enabled. Include the affected version or commit, impact, and a minimal reproduction. Avoid including real organisation data, device credentials, passwords, or private keys.

## Current safeguards

- The server binds to loopback by default and requires a TLS certificate for a non-loopback bind.
- Admin sessions use HTTP-only, Secure, SameSite cookies with an eight-hour lifetime.
- Admin write routes validate antiforgery tokens.
- Passwords are hashed with ASP.NET Core's password hasher.
- Device and join credentials are random; only hashes are used for authentication.
- The device credential retained by the server for post-approval recovery is protected by ASP.NET Core Data Protection.
- The Windows client encrypts its local registration state with Windows DPAPI for the current user.
- Approved-device checks are performed on HTTP requests and at SignalR connection time.
- Security-sensitive operations and alert acknowledgements are written to the audit table.

## Operational requirements

Use a certificate trusted by client devices, restrict the server port with the organisation firewall, protect the SQLite database and data-protection keys with Windows ACLs, and store backups in access-controlled organisation storage. Rotate the data-protection keys under an approved operational process and keep matching keys with database backups. The installer is per-user and starts the server after that user signs in; it does not run the alert client before interactive sign-in. Protect the Windows account and device because this account owns the server's DPAPI-protected keys.

## Known gaps

Public setup, login, and device claim routes have a fixed per-IP rate limit. The server has no database migration framework, multi-administrator account recovery, installer signature, secure update channel, or independent penetration test. The client is a foreground desktop app and does not yet have a service/session broker for reliable background delivery. See the limitations in the README.


