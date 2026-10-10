using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SafeNotify.Server.Data;

namespace SafeNotify.Server;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        app.MapPost("/api/client/join", async (JoinRequestInput input, SafeNotifyDbContext db) =>
        {
            var installationId = input.InstallationId.Trim();
            var name = input.DeviceName.Trim();
            var claim = input.ClaimToken.Trim();
            if (!Guid.TryParse(installationId, out _) || name.Length is < 1 or > 120 || claim.Length < 32) return Results.BadRequest(new { error = "Invalid registration details." });
            var device = await db.Devices.SingleOrDefaultAsync(x => x.InstallationId == installationId);
            if (device is { Status: "Approved" }) return Results.Conflict(new { error = "This installation is already registered." });
            var pending = await db.JoinRequests.Where(x => x.InstallationId == installationId && x.Status == "Pending").OrderByDescending(x => x.RequestedAt).FirstOrDefaultAsync();
            if (pending is not null)
            {
                if (!VerifyHash(claim, pending.ClaimHash)) return Results.Conflict(new { error = "A request for this installation is already pending." });
                return Results.Ok(new { requestId = pending.Id, status = pending.Status });
            }
            var request = new JoinRequest { InstallationId = installationId, DeviceName = name, ClaimHash = DeviceCredentialService.Hash(claim) };
            db.JoinRequests.Add(request);
            await db.SaveChangesAsync();
            return Results.Accepted($"/api/client/join/{request.Id}", new { requestId = request.Id, status = request.Status });
        }).RequireRateLimiting("public");
        app.MapPost("/api/client/join/{id:int}/status", async (int id, ClaimStatusInput input, SafeNotifyDbContext db, IDataProtectionProvider protection) =>
        {
            var request = await db.JoinRequests.SingleOrDefaultAsync(x => x.Id == id);
            if (request is null || !VerifyHash(input.ClaimToken, request.ClaimHash)) return Results.NotFound();
            var device = request.DeviceId is null ? null : await db.Devices.SingleOrDefaultAsync(x => x.Id == request.DeviceId && x.Status == "Approved");
            var credential = device?.ProtectedCredential is null ? null : protection.CreateProtector("SafeNotify.DeviceCredential").Unprotect(device.ProtectedCredential);
            return Results.Ok(new { status = request.Status, credential, deviceId = device?.Id });
        }).RequireRateLimiting("public");
        app.MapGet("/api/client/alerts", async (HttpContext context, SafeNotifyDbContext db) =>
        {
            var device = await Authenticate(context, db);
            if (device is null) return Results.Unauthorized();
            device.LastSeenAt = DateTimeOffset.UtcNow;
            var deliveries = await db.Deliveries.Include(x => x.Alert).Where(x => x.DeviceId == device.Id && (x.Status == "Offline" || x.Status == "Queued" || x.Status == "Sent" || x.Status == "Delivered")).ToListAsync();
            deliveries = deliveries.Where(x => x.Alert.ExpiresAt > DateTimeOffset.UtcNow).ToList();
            foreach (var item in deliveries) { item.Status = "Delivered"; item.UpdatedAt = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync();
            return Results.Ok(deliveries.Select(x => new { id = x.Id, deliveryId = x.Id, alertId = x.AlertId, x.Alert.Title, x.Alert.Message, x.Alert.Severity, x.Alert.SpeakMessage, x.Alert.ExpiresAt }));
        });
        app.MapPost("/api/client/deliveries/{id:int}/delivered", async (int id, HttpContext context, SafeNotifyDbContext db) =>
        {
            var device = await Authenticate(context, db);
            if (device is null) return Results.Unauthorized();
            var delivery = await db.Deliveries.Include(x => x.Alert).SingleOrDefaultAsync(x => x.Id == id && x.DeviceId == device.Id);
            if (delivery is null || delivery.Alert.ExpiresAt <= DateTimeOffset.UtcNow) return Results.NotFound();
            if (delivery.Status is "Sent" or "Offline" or "Queued") { delivery.Status = "Delivered"; delivery.UpdatedAt = DateTimeOffset.UtcNow; }
            device.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { delivered = true });
        });
        app.MapPost("/api/client/deliveries/{id:int}/acknowledge", async (int id, HttpContext context, SafeNotifyDbContext db) =>
        {
            var device = await Authenticate(context, db);
            if (device is null) return Results.Unauthorized();
            var delivery = await db.Deliveries.Include(x => x.Alert).SingleOrDefaultAsync(x => x.Id == id && x.DeviceId == device.Id);
            if (delivery is null || delivery.Alert.ExpiresAt <= DateTimeOffset.UtcNow) return Results.NotFound();
            delivery.Status = "Acknowledged";
            delivery.AcknowledgedAt ??= DateTimeOffset.UtcNow;
            delivery.UpdatedAt = DateTimeOffset.UtcNow;
            device.LastSeenAt = DateTimeOffset.UtcNow;
            db.AuditEntries.Add(new AuditEntry { DeviceId = device.Id, Action = "AlertAcknowledged", Details = $"Acknowledged alert {delivery.AlertId}." });
            await db.SaveChangesAsync();
            return Results.Ok(new { acknowledged = true });
        });
    }
    private static async Task<Device?> Authenticate(HttpContext context, SafeNotifyDbContext db)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[7..].Trim();
        if (token.Length < 32) return null;
        return await db.Devices.SingleOrDefaultAsync(x => x.CredentialHash == DeviceCredentialService.Hash(token) && x.Status == "Approved");
    }
    private static bool VerifyHash(string value, string expected)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(DeviceCredentialService.Hash(value)), Convert.FromHexString(expected)); }
        catch (FormatException) { return false; }
    }
}
public sealed record JoinRequestInput(string InstallationId, string DeviceName, string ClaimToken);
public sealed record ClaimStatusInput(string ClaimToken);







