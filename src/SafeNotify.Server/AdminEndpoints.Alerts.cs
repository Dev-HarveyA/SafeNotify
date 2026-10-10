using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SafeNotify.Server.Data;

namespace SafeNotify.Server;

public static partial class AdminEndpoints
{
    private static void MapAlertRoutes(RouteGroupBuilder admin)
    {
        admin.MapPost("/alerts", async (CreateAlert request, SafeNotifyDbContext db, HttpContext context, IHubContext<DeviceHub> hub, DevicePresence presence) =>
        {
            var title = request.Title.Trim();
            var message = request.Message.Trim();
            var severity = request.Severity.Trim().ToLowerInvariant();
            var audience = request.Audience.Trim();
            if (title.Length is < 1 or > 120 || message.Length is < 1 or > 4000 || severity is not ("message" or "warning" or "emergency") || audience is not ("All" or "Group" or "Device") || request.ExpiresMinutes is < 1 or > 1440) return Results.BadRequest(new { error = "Check the title, message, priority, audience, and expiry." });
            if (audience == "Group" && (request.GroupId is null || !await db.DeviceGroups.AnyAsync(x => x.Id == request.GroupId))) return Results.BadRequest(new { error = "Select a valid group." });
            if (audience == "Device" && (request.DeviceId is null || !await db.Devices.AnyAsync(x => x.Id == request.DeviceId && x.Status == "Approved"))) return Results.BadRequest(new { error = "Select an approved device." });
            var targets = await db.Devices.Where(x => x.Status == "Approved" && (audience == "All" || audience == "Group" && x.GroupId == request.GroupId || audience == "Device" && x.Id == request.DeviceId)).ToListAsync();
            if (targets.Count == 0) return Results.BadRequest(new { error = "There are no approved devices in this audience." });
            var alert = new Alert { Title = title, Message = message, Severity = severity, SpeakMessage = request.SpeakMessage, Audience = audience, GroupId = request.GroupId, DeviceId = request.DeviceId, CreatedById = int.Parse(context.User.FindFirst("adminId")!.Value), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(request.ExpiresMinutes) };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync();
            var deliveries = targets.Select(device => new Delivery { AlertId = alert.Id, DeviceId = device.Id, Status = presence.IsOnline(device.Id) ? "Sent" : "Offline" }).ToList();
            db.Deliveries.AddRange(deliveries);
            AdminEndpoints.AddAudit(db, context, "AlertSent", $"Sent {severity} alert {alert.Id} to {targets.Count} devices.");
            await db.SaveChangesAsync();
            foreach (var delivery in deliveries.Where(item => presence.IsOnline(item.DeviceId)))
            {
                await hub.Clients.Group(DeviceHub.GroupName(delivery.DeviceId)).SendAsync("AlertReceived", new { id = delivery.Id, deliveryId = delivery.Id, alertId = alert.Id, alert.Title, alert.Message, alert.Severity, alert.SpeakMessage, alert.ExpiresAt });
            }
            return Results.Created($"/api/admin/alerts/{alert.Id}", new { alert.Id, recipients = targets.Count, online = deliveries.Count(item => item.Status == "Sent"), offline = deliveries.Count(item => item.Status == "Offline") });
        });
        admin.MapGet("/alerts", async (SafeNotifyDbContext db) => Results.Ok(await db.Alerts.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.Title, x.Message, x.Severity, x.SpeakMessage, x.Audience, x.CreatedAt, x.ExpiresAt, recipients = db.Deliveries.Count(d => d.AlertId == x.Id), delivered = db.Deliveries.Count(d => d.AlertId == x.Id && d.Status != "Queued" && d.Status != "Offline"), acknowledged = db.Deliveries.Count(d => d.AlertId == x.Id && d.AcknowledgedAt != null), resolved = db.Deliveries.Count(d => d.AlertId == x.Id && d.ResolvedAt != null) }).ToListAsync()));
        admin.MapPost("/alerts/{id:int}/resolve", async (int id, SafeNotifyDbContext db, HttpContext context) =>
        {
            var alert = await db.Alerts.SingleOrDefaultAsync(x => x.Id == id && x.Severity == "emergency");
            if (alert is null) return Results.NotFound(new { error = "Emergency alert not found." });
            var deliveries = await db.Deliveries.Where(x => x.AlertId == id && x.ResolvedAt == null).ToListAsync();
            foreach (var delivery in deliveries) { delivery.Status = "ResolutionConfirmed"; delivery.ResolvedAt = DateTimeOffset.UtcNow; delivery.UpdatedAt = DateTimeOffset.UtcNow; }
            AdminEndpoints.AddAudit(db, context, "EmergencyResolutionConfirmed", $"Administrator confirmed resolution of alert {id}.");
            await db.SaveChangesAsync();
            return Results.Ok(new { resolved = true, recipients = deliveries.Count });
        });
        admin.MapGet("/alerts/{id:int}/deliveries", async (int id, SafeNotifyDbContext db) => Results.Ok(await db.Deliveries.Where(x => x.AlertId == id).Include(x => x.Device).OrderBy(x => x.Device.Name).Select(x => new { x.DeviceId, device = x.Device.Name, x.Status, x.UpdatedAt, x.AcknowledgedAt, x.ResolvedAt }).ToListAsync()));
    }
    private static void MapAuditAndSettingsRoutes(RouteGroupBuilder admin)
    {
        admin.MapGet("/audit", async (SafeNotifyDbContext db) => Results.Ok(await db.AuditEntries.OrderByDescending(x => x.CreatedAt).Take(500).Select(x => new { x.Id, x.Action, x.Details, x.CreatedAt, administrator = db.Administrators.Where(a => a.Id == x.AdministratorId).Select(a => a.Email).FirstOrDefault(), device = db.Devices.Where(d => d.Id == x.DeviceId).Select(d => d.Name).FirstOrDefault() }).ToListAsync()));
        admin.MapGet("/settings", async (SafeNotifyDbContext db) => Results.Ok(new { organisation = await db.Organisations.Select(x => x.Name).FirstOrDefaultAsync(), administrator = await db.Administrators.Select(x => x.Email).FirstOrDefaultAsync() }));
        admin.MapPut("/settings/organisation", async (RenameOrganisation request, SafeNotifyDbContext db, HttpContext context) =>
        {
            var organisation = await db.Organisations.FirstAsync();
            var name = request.Name.Trim();
            if (name.Length is < 2 or > 120) return Results.BadRequest(new { error = "Organisation name must be 2 to 120 characters." });
            organisation.Name = name;
            AdminEndpoints.AddAudit(db, context, "OrganisationRenamed", $"Changed organisation name to {name}.");
            await db.SaveChangesAsync();
            return Results.Ok(new { organisation = name });
        });
        admin.MapPost("/backup", async (SafeNotifyDbContext db, IConfiguration configuration) =>
        {
            var dataDirectory = configuration["SafeNotify:DataDirectory"] ?? "data";
            var directory = Path.Combine(dataDirectory, "backups");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"safenotify-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db");
            await db.Database.OpenConnectionAsync();
            try
            {
                var connection = (Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection();
                await using var destination = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file}");
                await destination.OpenAsync();
                connection.BackupDatabase(destination);
            }
            finally { await db.Database.CloseConnectionAsync(); }
            return Results.Ok(new { file = Path.GetFileName(file) });
        });
    }
}
public sealed record CreateAlert(string Title, string Message, string Severity, string Audience, int? GroupId, int? DeviceId, int ExpiresMinutes, bool SpeakMessage);
public sealed record RenameOrganisation(string Name);



