using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SafeNotify.Server.Data;

namespace SafeNotify.Server;

public static partial class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization();
        MapDeviceRoutes(admin);
        MapGroupRoutes(admin);
        MapAlertRoutes(admin);
        MapAuditAndSettingsRoutes(admin);
    }
    private static void MapDeviceRoutes(RouteGroupBuilder admin)
    {
        admin.MapGet("/overview", async (SafeNotifyDbContext db, DevicePresence presence) =>
        {
            var recent = await db.Alerts.OrderByDescending(x => x.CreatedAt).Take(5).Select(x => new { x.Id, x.Title, x.Severity, x.CreatedAt, recipients = db.Deliveries.Count(d => d.AlertId == x.Id) }).ToListAsync();
            return Results.Ok(new { server = "Online", pendingRequests = await db.JoinRequests.CountAsync(x => x.Status == "Pending"), approvedDevices = await db.Devices.CountAsync(x => x.Status == "Approved"), connectedDevices = presence.Count, recentAlerts = recent, totalAlerts = await db.Alerts.CountAsync(), now = DateTimeOffset.UtcNow });
        });
        admin.MapGet("/requests", async (SafeNotifyDbContext db) => Results.Ok(await db.JoinRequests.OrderByDescending(x => x.RequestedAt).Select(x => new { x.Id, x.DeviceName, x.Status, x.RequestedAt }).ToListAsync()));
        admin.MapPost("/requests/{id:int}/approve", async (int id, SafeNotifyDbContext db, HttpContext context, IDataProtectionProvider protection) =>
        {
            var request = await db.JoinRequests.SingleOrDefaultAsync(x => x.Id == id);
            if (request is null) return Results.NotFound();
            if (request.Status != "Pending") return Results.Conflict(new { error = "Request has already been reviewed." });
            var device = await db.Devices.SingleOrDefaultAsync(x => x.InstallationId == request.InstallationId);
            if (device is { Status: "Approved" }) return Results.Conflict(new { error = "This device is already approved." });
            var credential = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
            device ??= new Device { InstallationId = request.InstallationId, Name = request.DeviceName, CredentialHash = DeviceCredentialService.Hash(credential) };
            device.Name = request.DeviceName;
            device.Status = "Approved";
            device.CredentialHash = DeviceCredentialService.Hash(credential);
            device.ProtectedCredential = protection.CreateProtector("SafeNotify.DeviceCredential").Protect(credential);
            device.RevokedAt = null;
            if (device.Id == 0) db.Devices.Add(device);
            request.Status = "Approved";
            request.DecidedAt = DateTimeOffset.UtcNow;
            request.DecidedById = int.Parse(context.User.FindFirst("adminId")!.Value);
            await db.SaveChangesAsync();
            request.DeviceId = device.Id;
            db.AuditEntries.Add(new AuditEntry { AdministratorId = request.DecidedById, DeviceId = device.Id, Action = "DeviceApproved", Details = $"Approved device {device.Name}." });
            await db.SaveChangesAsync();
            return Results.Ok(new { requestId = request.Id, approved = true });
        });
        admin.MapPost("/requests/{id:int}/reject", async (int id, SafeNotifyDbContext db, HttpContext context) =>
        {
            var request = await db.JoinRequests.SingleOrDefaultAsync(x => x.Id == id);
            if (request is null) return Results.NotFound();
            if (request.Status != "Pending") return Results.Conflict(new { error = "Request has already been reviewed." });
            request.Status = "Rejected";
            request.DecidedAt = DateTimeOffset.UtcNow;
            request.DecidedById = int.Parse(context.User.FindFirst("adminId")!.Value);
            db.AuditEntries.Add(new AuditEntry { AdministratorId = request.DecidedById, Action = "DeviceRejected", Details = $"Rejected request for {request.DeviceName}." });
            await db.SaveChangesAsync();
            return Results.Ok(new { rejected = true });
        });
        admin.MapGet("/devices", async (SafeNotifyDbContext db, DevicePresence presence) => Results.Ok(await db.Devices.Include(x => x.Group).OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.Name, x.Status, x.CreatedAt, x.LastSeenAt, group = x.Group == null ? null : x.Group.Name, online = presence.IsOnline(x.Id) }).ToListAsync()));
        admin.MapPatch("/devices/{id:int}", async (int id, RenameDevice request, SafeNotifyDbContext db, HttpContext context) =>
        {
            var device = await db.Devices.SingleOrDefaultAsync(x => x.Id == id);
            if (device is null) return Results.NotFound();
            var name = request.Name.Trim();
            if (name.Length is < 1 or > 120) return Results.BadRequest(new { error = "Device name must be 1 to 120 characters." });
            device.Name = name;
            AddAudit(db, context, "DeviceRenamed", $"Renamed device to {name}.", id);
            await db.SaveChangesAsync();
            return Results.Ok(new { device.Id, device.Name });
        });
        admin.MapPost("/devices/{id:int}/revoke", async (int id, SafeNotifyDbContext db, HttpContext context, DevicePresence presence, Microsoft.AspNetCore.SignalR.IHubContext<DeviceHub> hub) =>
        {
            var device = await db.Devices.SingleOrDefaultAsync(x => x.Id == id);
            if (device is null) return Results.NotFound();
            device.Status = "Revoked";
            device.RevokedAt = DateTimeOffset.UtcNow;
            device.CredentialHash = DeviceCredentialService.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)));
            device.ProtectedCredential = null;
            AddAudit(db, context, "DeviceRevoked", $"Revoked device {device.Name}.", id);
            await db.SaveChangesAsync();
            await presence.RevokeAsync(id, hub);
            return Results.Ok(new { revoked = true });
        });
        admin.MapPut("/devices/{id:int}/group", async (int id, AssignGroup request, SafeNotifyDbContext db, HttpContext context) =>
        {
            var device = await db.Devices.SingleOrDefaultAsync(x => x.Id == id);
            if (device is null) return Results.NotFound();
            if (request.GroupId is not null && !await db.DeviceGroups.AnyAsync(x => x.Id == request.GroupId)) return Results.NotFound(new { error = "Group not found." });
            device.GroupId = request.GroupId;
            AddAudit(db, context, "DeviceGroupChanged", $"Updated group for device {device.Name}.", id);
            await db.SaveChangesAsync();
            return Results.Ok(new { device.Id, device.GroupId });
        });
    }
    private static void MapGroupRoutes(RouteGroupBuilder admin)
    {
        admin.MapGet("/groups", async (SafeNotifyDbContext db) => Results.Ok(await db.DeviceGroups.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, deviceCount = db.Devices.Count(d => d.GroupId == x.Id && d.Status == "Approved") }).ToListAsync()));
        admin.MapPost("/groups", async (CreateGroup request, SafeNotifyDbContext db, HttpContext context) =>
        {
            var name = request.Name.Trim();
            if (name.Length is < 2 or > 80) return Results.BadRequest(new { error = "Group name must be 2 to 80 characters." });
            if (await db.DeviceGroups.AnyAsync(x => x.Name == name)) return Results.Conflict(new { error = "Group already exists." });
            var group = new DeviceGroup { Name = name };
            db.DeviceGroups.Add(group);
            AddAudit(db, context, "GroupCreated", $"Created group {name}.");
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/groups/{group.Id}", new { group.Id, group.Name });
        });
        admin.MapDelete("/groups/{id:int}", async (int id, SafeNotifyDbContext db, HttpContext context) =>
        {
            var group = await db.DeviceGroups.SingleOrDefaultAsync(x => x.Id == id);
            if (group is null) return Results.NotFound();
            db.DeviceGroups.Remove(group);
            AddAudit(db, context, "GroupDeleted", $"Deleted group {group.Name}.");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
    private static void AddAudit(SafeNotifyDbContext db, HttpContext context, string action, string details, int? deviceId = null)
    {
        var adminId = context.User.FindFirst("adminId")?.Value;
        db.AuditEntries.Add(new AuditEntry { AdministratorId = adminId is null ? null : int.Parse(adminId), DeviceId = deviceId, Action = action, Details = details });
    }
}
public sealed record RenameDevice(string Name);
public sealed record AssignGroup(int? GroupId);
public sealed record CreateGroup(string Name);


