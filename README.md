# SafeNotify

SafeNotify is a self-hosted alert and messaging system for Windows organisations. An administrator runs the server and dashboard. Windows clients request access, wait for approval, and receive messages, warnings, and emergency alerts.

## [Download the Windows installer from GitHub Releases](https://github.com/Dev-HarveyA/SafeNotify/releases/latest)

On the Releases page, open the latest version and download `SafeNotify-Setup-x.y.z-win-x64.exe` from **Assets**. Run the installer to install SafeNotify or upgrade an existing installation. Choose **Client and server**, **Client only**, or **Server only** in setup.

If there is no installer under **Assets**, a release has not finished building yet. Releases are created when a `vMAJOR.MINOR.PATCH` version tag is pushed. Check [release build status](https://github.com/Dev-HarveyA/SafeNotify/actions/workflows/release.yml) for progress or errors. SafeNotify does not install updates automatically; download and run each new installer to upgrade.

**New to SafeNotify?** Install the Windows installer above. The source-build instructions below are for developers.

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
- The prebuilt installer includes what SafeNotify needs to run.
- .NET 10 SDK and Inno Setup 6 are needed only to build from source.
- A trusted TLS certificate for use across a LAN.

## Try SafeNotify on one computer

1. Install the .NET 10 SDK and open PowerShell in the repository folder.
2. Double-click `Start-Server.bat` in the repository folder. A console window shows the server status. Leave that window open while using SafeNotify. To stop it, press **Ctrl+C** in the server window, or double-click `Stop-Server.bat`.

   You can also start it from PowerShell:

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

Most users should download the installer from [the latest GitHub Release](https://github.com/Dev-HarveyA/SafeNotify/releases/latest). The following steps are for building the installer yourself.

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

The server binds to `127.0.0.1` by default. To set up a small LAN, connect the server and client computers to the same Wi-Fi or Ethernet network, then open **Dashboard → Settings → LAN access** and select **Set up LAN automatically**. Confirm the prompt to create and trust a local certificate authority for your Windows account. SafeNotify creates a server HTTPS certificate for the current network addresses, configures LAN mode, and adds a Windows Firewall rule limited to the local subnet and Private or Domain networks. Approve the Windows Firewall prompt when it appears. The dashboard then opens at its HTTPS address.

SafeNotify downloads `SafeNotify-LAN.cer`. The server trusts the certificate for the current Windows user. Install the public certificate on each client computer:

1. Open the downloaded `.cer` file.
2. Select **Current User** when Windows asks where to install it.
3. Choose **Place all certificates in the following store**, select **Trusted Root Certification Authorities**, and finish the wizard.
4. Open the HTTPS address shown by SafeNotify. In the client, enter the same address, find the organisation, and request to join. Approve the request in **Device requests**.

The certificate contains no private key. Never share the server's private key or upload it to GitHub. A self-signed certificate is for a controlled LAN; each Windows user must explicitly trust its public certificate. Reserve the server's IP address in the router so the certificate and connection address remain valid. To turn LAN access off, use the LAN access control in Settings. SafeNotify removes its firewall rule, returns to loopback binding, and restarts. Do not expose the dashboard to the internet.

For a workplace deployment, use a certificate from your organisation's certificate authority instead. Install the certificate and private key in the server user's **Current User → Personal** certificate store, and install the issuing certificate chain on each client computer.

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
| `Start-Server.bat`, `Stop-Server.bat` | Start and stop the source server on Windows |
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
