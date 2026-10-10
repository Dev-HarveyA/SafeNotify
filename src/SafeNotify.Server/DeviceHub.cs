using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SafeNotify.Server.Data;

namespace SafeNotify.Server;

public sealed class DevicePresence
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> connections = new();
    private readonly ConcurrentDictionary<string, int> deviceByConnection = new();
    public int Count => connections.Count;
    public bool IsOnline(int id) => connections.TryGetValue(id, out var active) && !active.IsEmpty;
    public void Add(int id, string connectionId)
    {
        deviceByConnection[connectionId] = id;
        connections.GetOrAdd(id, _ => new ConcurrentDictionary<string, byte>())[connectionId] = 0;
    }
    public void RemoveConnection(string connectionId)
    {
        if (!deviceByConnection.TryRemove(connectionId, out var id) || !connections.TryGetValue(id, out var active)) return;
        active.TryRemove(connectionId, out _);
        if (active.IsEmpty) ((ICollection<KeyValuePair<int, ConcurrentDictionary<string, byte>>>)connections).Remove(new KeyValuePair<int, ConcurrentDictionary<string, byte>>(id, active));
    }
    public async Task RevokeAsync(int id, IHubContext<DeviceHub> hub)
    {
        if (!connections.TryRemove(id, out var active)) return;
        foreach (var connectionId in active.Keys)
        {
            deviceByConnection.TryRemove(connectionId, out _);
            await hub.Clients.Client(connectionId).SendAsync("AccessRevoked");
            await hub.Groups.RemoveFromGroupAsync(connectionId, DeviceHub.GroupName(id));
        }
    }
}
public sealed class DeviceHub(SafeNotifyDbContext db, DevicePresence presence) : Hub
{
    public static string GroupName(int deviceId) => $"device:{deviceId}";
    public override async Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
        if (string.IsNullOrWhiteSpace(token)) { Context.Abort(); return; }
        var hash = DeviceCredentialService.Hash(token);
        var device = await db.Devices.SingleOrDefaultAsync(x => x.CredentialHash == hash && x.Status == "Approved");
        if (device is null) { Context.Abort(); return; }
        device.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        presence.Add(device.Id, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(device.Id));
        await base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        presence.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed class AlertExpiryService(IServiceScopeFactory scopeFactory, ILogger<AlertExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SafeNotifyDbContext>();
                var now = DateTimeOffset.UtcNow;
                var expired = await db.Deliveries.Include(x => x.Alert).ToListAsync(stoppingToken);
                expired = expired.Where(x => x.Alert.ExpiresAt <= now && x.Status != "Expired" && x.Status != "Acknowledged" && x.Status != "ResolutionConfirmed").ToList();
                foreach (var delivery in expired) { delivery.Status = "Expired"; delivery.UpdatedAt = DateTimeOffset.UtcNow; }
                if (expired.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { logger.LogError(exception, "Alert expiry processing failed."); }
        }
    }
}

