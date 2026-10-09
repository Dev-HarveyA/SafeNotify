using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SafeNotify.Server;

public static class LanNetworkEndpoints
{
    private const string FirewallRuleName = "SafeNotify LAN access";
    private const string CertificateFileName = "SafeNotify-LAN.cer";

    public static void MapLanNetworkEndpoints(this WebApplication app, string settingsPath, string bindAddress, int port, bool certificateConfigured)
    {
        var admin = app.MapGroup("/api/admin/network").RequireAuthorization();
        var certificatePath = Path.Combine(Path.GetDirectoryName(settingsPath)!, CertificateFileName);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SafeNotify.Lan");

        admin.MapGet("/lan", () => Results.Ok(new LanNetworkState(IsEnabled(settingsPath, bindAddress), GetAddresses(), port, certificateConfigured, File.Exists(certificatePath))));
        admin.MapGet("/lan/certificate", () => File.Exists(certificatePath)
            ? Results.File(certificatePath, "application/pkix-cert", CertificateFileName)
            : Results.NotFound(new { error = "No SafeNotify LAN certificate is available to download." }));

        admin.MapPost("/lan/setup", async () =>
        {
            if (!OperatingSystem.IsWindows()) return Results.Conflict(new { error = "Automatic LAN setup is supported on Windows only." });
            if (certificateConfigured) return Results.Conflict(new { error = "A HTTPS certificate is already configured for this server." });

            var addresses = GetAddresses();
            if (addresses.Length == 0) return Results.Conflict(new { error = "No active LAN IPv4 address was found. Connect this computer to Wi-Fi or Ethernet and try again." });

            LanCertificates? certificates = null;
            var firewallConfigured = false;
            var oldSettings = File.Exists(settingsPath) ? await File.ReadAllBytesAsync(settingsPath) : null;
            var oldCertificateFile = File.Exists(certificatePath) ? await File.ReadAllBytesAsync(certificatePath) : null;

            try
            {
                certificates = CreateLanCertificates(addresses, logger);
                await ConfigureFirewallAsync(true, port);
                firewallConfigured = true;

                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                await WriteAtomicAsync(certificatePath, certificates.Root.Export(X509ContentType.Cert));
                await WriteNetworkSettingsAsync(settingsPath, "0.0.0.0", certificates.Server.Thumbprint);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SafeNotify automatic LAN setup failed.");
                if (firewallConfigured)
                {
                    try { await ConfigureFirewallAsync(false, port); }
                    catch (Exception rollbackException) { logger.LogError(rollbackException, "SafeNotify could not remove its firewall rule after LAN setup failed."); }
                }
                await RestoreFileAsync(settingsPath, oldSettings, logger);
                await RestoreFileAsync(certificatePath, oldCertificateFile, logger);
                if (certificates is not null)
                {
                    RemoveCertificate(certificates.Server, StoreName.My, logger);
                    RemoveCertificate(certificates.Root, StoreName.Root, logger);
                    RemoveCertificate(certificates.Root, StoreName.My, logger);
                    certificates.Dispose();
                }
                return Results.Problem("SafeNotify could not complete LAN setup. Check the Windows Firewall prompt and server log, then try again.", statusCode: StatusCodes.Status500InternalServerError);
            }

            var publicCertificate = Convert.ToBase64String(certificates!.Root.Export(X509ContentType.Cert));
            var address = $"https://{addresses[0]}:{port}";
            var thumbprint = certificates.Server.Thumbprint;
            certificates.Dispose();

            try
            {
                ScheduleRestart(app);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.LogError(exception, "SafeNotify LAN setup completed but the server could not restart automatically.");
                return Results.Problem("LAN setup was saved, but the server could not restart automatically. Restart SafeNotify Server manually.", statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(new LanSetupResult(address, thumbprint, publicCertificate, true));
        });

        admin.MapPut("/lan", async ([FromBody] LanModeRequest request) =>
        {
            if (request.Enabled && !certificateConfigured) return Results.Conflict(new { error = "Set up or install a trusted HTTPS certificate before enabling LAN access." });
            if (!OperatingSystem.IsWindows()) return Results.Conflict(new { error = "Automatic LAN firewall setup is currently supported on Windows only." });
            try
            {
                await ConfigureFirewallAsync(request.Enabled, port);
                await WriteNetworkSettingsAsync(settingsPath, request.Enabled ? "0.0.0.0" : "127.0.0.1", ReadConfiguredThumbprint(settingsPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
            {
                logger.LogError(exception, "SafeNotify LAN network settings could not be applied.");
                return Results.Problem("Windows could not apply the LAN network settings. No server setting was changed.", statusCode: StatusCodes.Status500InternalServerError);
            }

            try
            {
                ScheduleRestart(app);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.LogError(exception, "SafeNotify LAN settings were saved but the server could not restart automatically.");
                return Results.Problem("LAN settings were saved, but the server could not schedule its restart. Restart SafeNotify Server manually.", statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(new LanNetworkState(request.Enabled, GetAddresses(), port, certificateConfigured, File.Exists(certificatePath)) { Restarting = true });
        });
    }

    private static LanCertificates CreateLanCertificates(string[] addresses, ILogger logger)
    {
        var machineName = Dns.GetHostName();
        using var rootRsa = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=SafeNotify LAN Root", rootRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using var generatedRoot = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        var rootCertificate = PersistCertificate(generatedRoot);

        try
        {
            InstallRootTrust(rootCertificate);
            using var serverRsa = RSA.Create(2048);
            var serverRequest = new CertificateRequest($"CN={machineName}", serverRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            serverRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(serverRequest.PublicKey, false));
            var enhancedKeyUsage = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
            serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsage, false));

            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName(machineName);
            try
            {
                var fullyQualifiedName = Dns.GetHostEntry(machineName).HostName;
                if (!fullyQualifiedName.Equals(machineName, StringComparison.OrdinalIgnoreCase)) subjectAlternativeNames.AddDnsName(fullyQualifiedName);
            }
            catch (SocketException) { }
            foreach (var address in addresses)
            {
                if (IPAddress.TryParse(address, out var ipAddress)) subjectAlternativeNames.AddIpAddress(ipAddress);
            }
            serverRequest.CertificateExtensions.Add(subjectAlternativeNames.Build());

            var publicServerCertificate = serverRequest.Create(rootCertificate, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2), RandomNumberGenerator.GetBytes(16));
            using var serverCertificateWithPrivateKey = publicServerCertificate.CopyWithPrivateKey(serverRsa);
            using (publicServerCertificate)
            {
                var serverCertificate = PersistCertificate(serverCertificateWithPrivateKey);
                return new LanCertificates(rootCertificate, serverCertificate);
            }
        }
        catch
        {
            RemoveCertificate(rootCertificate, StoreName.Root, logger);
            RemoveCertificate(rootCertificate, StoreName.My, logger);
            rootCertificate.Dispose();
            throw;
        }
    }

    private static X509Certificate2 PersistCertificate(X509Certificate2 generatedCertificate)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pfx = generatedCertificate.Export(X509ContentType.Pkcs12, password);
        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            return certificate;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    private sealed class LanCertificates(X509Certificate2 root, X509Certificate2 server) : IDisposable
    {
        public X509Certificate2 Root { get; } = root;
        public X509Certificate2 Server { get; } = server;

        public void Dispose()
        {
            Root.Dispose();
            Server.Dispose();
        }
    }

    private static string[] GetAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address) && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
        .Select(address => address.Address.ToString())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static bool IsEnabled(string settingsPath, string bindAddress)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.GetProperty("SafeNotify").GetProperty("BindAddress").GetString() == "0.0.0.0";
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return bindAddress != "127.0.0.1"; }
    }

    private static string? ReadConfiguredThumbprint(string settingsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.GetProperty("SafeNotify").TryGetProperty("CertificateThumbprint", out var value) ? value.GetString() : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    private static async Task WriteNetworkSettingsAsync(string settingsPath, string bindAddress, string? thumbprint)
    {
        var safeNotify = new Dictionary<string, string> { ["BindAddress"] = bindAddress };
        if (!string.IsNullOrWhiteSpace(thumbprint)) safeNotify["CertificateThumbprint"] = thumbprint;
        var settings = JsonSerializer.Serialize(new Dictionary<string, object> { ["SafeNotify"] = safeNotify }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await WriteAtomicAsync(settingsPath, System.Text.Encoding.UTF8.GetBytes(settings));
    }

    private static async Task WriteAtomicAsync(string path, byte[] content)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task RestoreFileAsync(string path, byte[]? previousContent, ILogger logger)
    {
        try
        {
            if (previousContent is null)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else
            {
                await WriteAtomicAsync(path, previousContent);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "SafeNotify could not restore a configuration file after LAN setup failed.");
        }
    }

    private static void InstallRootTrust(X509Certificate2 rootCertificate)
    {
        using var publicCertificate = X509CertificateLoader.LoadCertificate(rootCertificate.RawData);
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(publicCertificate);
    }

    private static void RemoveCertificate(X509Certificate2 certificate, StoreName storeName, ILogger logger)
    {
        try
        {
            using var store = new X509Store(storeName, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(certificate);
        }
        catch (Exception exception) when (exception is CryptographicException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "SafeNotify could not remove a certificate after LAN setup failed.");
        }
    }

    private static async Task ConfigureFirewallAsync(bool enabled, int port)
    {
        var arguments = enabled
            ? $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port} remoteip=localsubnet profile=private,domain"
            : $"advfirewall firewall delete rule name=\"{FirewallRuleName}\"";
        var process = Process.Start(new ProcessStartInfo("netsh.exe", arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new InvalidOperationException("Could not start Windows Firewall configuration.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows Firewall did not accept the LAN rule.");
    }

    private static void ScheduleRestart(WebApplication app)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("The running server executable could not be located.");
        var restart = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1)) restart.ArgumentList.Add(argument);
        restart.ArgumentList.Add("--safe-notify-restart-wait");
        if (Process.Start(restart) is null) throw new InvalidOperationException("The server restart helper could not be started.");
        app.Lifetime.StopApplication();
    }
}

public sealed record LanModeRequest(bool Enabled);
public sealed record LanNetworkState(bool Enabled, string[] Addresses, int Port, bool CertificateConfigured, bool CertificateDownloadAvailable)
{
    public bool Restarting { get; init; }
}
public sealed record LanSetupResult(string Address, string Thumbprint, string Certificate, bool Restarting);
