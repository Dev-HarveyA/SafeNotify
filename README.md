# SafeNotify

SafeNotify is a self-hosted alert and messaging system for Windows organisations. An administrator runs the server and dashboard. Windows clients request access, wait for approval, and receive messages, warnings, and emergency alerts.

> SafeNotify is an early development project. It is not certified or independently assessed for life-safety use. Do not use it as a replacement for approved emergency procedures or certified alarm systems.

## What it includes

- ASP.NET Core server, web dashboard, SQLite database, and SignalR device channel.
- Windows desktop client built with WPF.
- Administrator approval for new devices, device management, groups, and audit history.
- Message, warning, and emergency alert delivery with acknowledgement and resolution tracking.
- Local-only server binding by default. LAN binding requires HTTPS.
- MIT licence.

## Requirements

- Windows 10 or Windows 11.
- .NET 10 SDK to run from source or build the applications.
- Inno Setup 6 to build the Windows installer. It is not required to install or run SafeNotify.
- A trusted TLS certificate for use across a LAN.

## Try SafeNotify on one computer

1. Install the .NET 10 SDK and open PowerShell in the repository folder.
2. Start the server:

   ```powershell
   .\scripts\Start-Server.ps1
   ```

3. Open [http://127.0.0.1:5080](http://127.0.0.1:5080). On first use, create the organisation and administrator account. Use a password with at least 14 characters. Initial setup closes after the account is created.
4. Open a second PowerShell window in the repository folder and start the client:

   ```powershell
   .\scripts\Start-Client.ps1
   ```

5. In the client, choose **Find organisation**, then **Request to join**. In the dashboard, open **Device requests** and approve the request. The client will then connect and receive alerts.

The client defaults to `http://127.0.0.1:5080`, which works only on the same computer. If PowerShell blocks a script, start it with a one-time execution policy override:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-Server.ps1
```

Server data is stored under `%LOCALAPPDATA%\SafeNotify\Server`. It contains the database and the DPAPI-protected key ring. Keep these files together in backups and run the server under the same Windows account that created them.

## Install SafeNotify on Windows

On the build computer, install Inno Setup 6. With WinGet:

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

Then build the installer from the repository folder:

```powershell
.\scripts\Build-Installer.ps1
```

The installer is created at `publish\installer\SafeNotify-Setup-1.0.0-win-x64.exe`. It installs per-user without administrator rights. Choose client-only, server-only, or both. Setup creates Start menu shortcuts and lets you enable automatic startup for each selected component. Startup runs when the installing Windows user signs in. The alert client needs an interactive sign-in to show alerts.

The installed server runs without a console window. It writes logs to `%LOCALAPPDATA%\SafeNotify\Logs`. Its configuration is in `%LOCALAPPDATA%\Programs\SafeNotify\server\appsettings.json`. Its database and key ring remain under `%LOCALAPPDATA%\SafeNotify\Server` when the application is upgraded or uninstalled.

The installer is unsigned. Windows may show a SmartScreen warning. Sign installers with an organisation-controlled code-signing certificate before distributing them to other people. Do not upload the generated installer or published binaries as part of the source repository; publish signed installers separately as GitHub Releases.

## Use SafeNotify across a LAN

The server binds to `127.0.0.1` by default. LAN mode is controlled from **Dashboard → Settings → LAN access**. Before enabling it, configure a trusted HTTPS certificate for the server name or IP address, and install its issuing certificate chain on each client computer. The dashboard then adds a Windows Firewall rule limited to the local subnet and Private or Domain network profiles, binds the server to the LAN, and restarts it. It displays the HTTPS address and port for approved users on the same Wi-Fi/LAN.

1. Obtain a certificate from your organisation's certificate authority. Its subject alternative name must match the server name or IP address clients will use.
2. Install the certificate and private key in the server user's **Current User → Personal** certificate store. Set `SafeNotify:CertificateThumbprint` in the server configuration.
3. Open **Settings → LAN access** and select **Enable LAN access**. Approve the Windows Firewall prompt. The server restarts automatically.
4. Give approved users the HTTPS address and port shown in the dashboard. Their devices must be connected to the same organisation Wi-Fi/LAN.
5. In each client, enter the HTTPS address, find the organisation, and request to join. Approve each new device in **Device requests**.

For a development run from source, the server script can load a PFX file and prompt for its password without saving it in the repository:

```powershell
.\scripts\Start-Server.ps1 -BindAddress 192.168.1.20 -Port 5080 -CertificatePath C:\SafeNotify\certificates\server.pfx
```

To turn LAN access off, use the same dashboard control. SafeNotify removes its firewall rule, returns to loopback binding, and restarts. The server refuses non-loopback HTTP. The client validates Windows TLS certificates normally. Do not expose the dashboard to the internet. The LAN address can change if the router assigns a different IP; configure a DHCP reservation if the address needs to remain stable.

## Troubleshooting

- **The client cannot find the organisation:** Confirm the server is running. `http://127.0.0.1:5080` works only on the server computer. A client on another computer must use the server's HTTPS name and port.
- **Request to join is unavailable:** First choose **Find organisation** and wait for the server to be found. Then submit the request and approve it in the dashboard's **Device requests** page.
- **The request is pending:** The administrator must approve it. Keep the client open and connected while approval is pending.
- **A server error appears:** Check `%LOCALAPPDATA%\SafeNotify\Logs\server-yyyyMMdd.log` on the server computer. Do not post database files, key-ring files, certificates, or credentials in a public issue.
- **The server does not start after sign-in:** Check that SafeNotify Server is enabled in Windows Startup Apps and review the server log folder above.

## Move existing development data to the installer

If you already ran SafeNotify from the source folder and want to keep its organisation, devices, and approvals when switching to the installer:

1. Stop the source server.
2. Run this once from the repository folder:

   ```powershell
   .\scripts\Migrate-ExistingData.ps1
   ```

The script copies the database consistently and copies its matching DPAPI key ring to `%LOCALAPPDATA%\SafeNotify\Server`. The source data remains in place. Do not upload either copy to GitHub.

## Build and run the checks

```powershell
dotnet restore .\SafeNotify.slnx
dotnet build .\SafeNotify.slnx --configuration Release
dotnet test .\SafeNotify.slnx --configuration Release
```

To create self-contained Windows x64 application files, run `.\scripts\Publish.ps1`. To create the installer, run `.\scripts\Build-Installer.ps1`; it publishes the applications as part of the build.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/SafeNotify.Server` | ASP.NET Core server, API, dashboard, and SQLite models |
| `src/SafeNotify.Client` | WPF Windows client and alert windows |
| `tests/SafeNotify.Server.Tests` | Server API workflow tests |
| `scripts` | Development launchers, data migration, publishing, and installer build |
| `config` | Example configuration |
| `docs` | Architecture notes |
| `.github/workflows` | Windows build and test workflow |

## Data, backups, and recovery

The server stores `safenotify.db` and its data-protection keys beneath `%LOCALAPPDATA%\SafeNotify\Server`. Back them up together to access-controlled storage. A database restored without its matching key ring cannot decrypt protected device credentials. Use **Settings → Database backup** for a consistent SQLite backup, and restore only while the server is stopped.

The repository ignores local databases, key rings, certificates, build outputs, and installer files. Keep real organisation data and private certificates out of GitHub.

## Known limitations

- The database schema is initialized with EF Core `EnsureCreated`; there is no schema migration system yet.
- Only one initial administrator account is supported. There is no account recovery flow.
- Network discovery, secure update delivery, and installer signing are not configured.
- The client is a foreground Windows app. It needs a signed-in user session and has no service/session broker or tray mode.
- SafeNotify has not had an independent security, reliability, or multi-computer deployment assessment.

Review [SECURITY.md](SECURITY.md) and [docs/architecture.md](docs/architecture.md) before exposing a server to a LAN.

Pushing a version tag such as `v1.2.0` starts the Windows release workflow, builds the installer, and attaches it to a GitHub Release.

## Licence

SafeNotify is distributed under the MIT License. See [LICENSE](LICENSE).
