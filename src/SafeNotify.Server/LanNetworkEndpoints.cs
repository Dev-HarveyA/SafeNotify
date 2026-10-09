using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SafeNotify.Server;

public static class LanNetworkEndpoints
{
    private const string FirewallRuleName = "SafeNotify LAN access";

    public static void MapLanNetworkEndpoints(this WebApplication app, string settingsPath, string bindAddress, int port, bool certificateConfigured)
    {
        var admin = app.MapGroup("/api/admin/network").RequireAuthorization();
        admin.MapGet("/lan", () => Results.Ok(new LanNetworkState(IsEnabled(settingsPath, bindAddress), GetAddresses(), port, certificateConfigured)));
        admin.MapPut("/lan", async ([FromBody] LanModeRequest request) =>
        {
            if (request.Enabled && !certificateConfigured) return Results.Conflict(new { error = "Install a trusted HTTPS certificate before enabling LAN access." });
            if (!OperatingSystem.IsWindows()) return Results.Conflict(new { error = "Automatic LAN firewall setup is currently supported on Windows only." });
            try
            {
                await ConfigureFirewallAsync(request.Enabled, port);
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                var settings = JsonSerializer.Serialize(new { SafeNotify = new { BindAddress = request.Enabled ? "0.0.0.0" : "127.0.0.1" } });
                await File.WriteAllTextAsync(settingsPath, settings);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return Results.Problem("Windows could not apply the LAN network settings. No server setting was changed.", statusCode: StatusCodes.Status500InternalServerError);
            }
            _ = RestartServerAsync(app);
            return Results.Ok(new LanNetworkState(request.Enabled, GetAddresses(), port, certificateConfigured) { Restarting = true });
        });
    }

    private static bool IsEnabled(string settingsPath, string bindAddress)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.GetProperty("SafeNotify").GetProperty("BindAddress").GetString() == "0.0.0.0";
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return bindAddress != "127.0.0.1"; }
    }

    private static string[] GetAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address) && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
        .Select(address => address.Address.ToString())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static async Task ConfigureFirewallAsync(bool enabled, int port)
    {
        var arguments = enabled
            ? $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port} remoteip=localsubnet profile=private,domain"
            : $"advfirewall firewall delete rule name=\"{FirewallRuleName}\"";
        var process = Process.Start(new ProcessStartInfo("netsh.exe", arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new InvalidOperationException("Could not start Windows Firewall configuration.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows Firewall did not accept the LAN rule.");
    }

    private static async Task RestartServerAsync(WebApplication app)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        app.Lifetime.StopApplication();
        await Task.Delay(TimeSpan.FromSeconds(3));
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;
        var restart = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1)) restart.ArgumentList.Add(argument);
        Process.Start(restart);
    }
}

public sealed record LanModeRequest(bool Enabled);
public sealed record LanNetworkState(bool Enabled, string[] Addresses, int Port, bool CertificateConfigured)
{
    public bool Restarting { get; init; }
}
