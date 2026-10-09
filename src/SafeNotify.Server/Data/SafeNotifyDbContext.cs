using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace SafeNotify.Server.Data;
public sealed class SafeNotifyDbContext(DbContextOptions<SafeNotifyDbContext> options) : DbContext(options)
{
 public DbSet<Organisation> Organisations => Set<Organisation>();
 public DbSet<Administrator> Administrators => Set<Administrator>();
 public DbSet<Device> Devices => Set<Device>();
 public DbSet<JoinRequest> JoinRequests => Set<JoinRequest>();
 public DbSet<DeviceGroup> DeviceGroups => Set<DeviceGroup>();
 public DbSet<Alert> Alerts => Set<Alert>();
 public DbSet<Delivery> Deliveries => Set<Delivery>();
 public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
 protected override void OnModelCreating(ModelBuilder modelBuilder)
 {
  var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(value => value.UtcDateTime.Ticks, value => new DateTimeOffset(value, TimeSpan.Zero));
  var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(value => value.HasValue ? value.Value.UtcDateTime.Ticks : null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
  foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
  {
   if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(dateTimeOffsetConverter);
   if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(nullableDateTimeOffsetConverter);
  }
  modelBuilder.Entity<Organisation>().HasIndex(x => x.Name).IsUnique();
  modelBuilder.Entity<Administrator>().HasIndex(x => x.Email).IsUnique();
  modelBuilder.Entity<Device>().HasIndex(x => x.InstallationId).IsUnique();
  modelBuilder.Entity<Device>().HasIndex(x => x.CredentialHash).IsUnique();
  modelBuilder.Entity<DeviceGroup>().HasIndex(x => x.Name).IsUnique();
  modelBuilder.Entity<JoinRequest>().HasIndex(x => new { x.InstallationId, x.Status });
  modelBuilder.Entity<Delivery>().HasIndex(x => new { x.AlertId, x.DeviceId }).IsUnique();
  modelBuilder.Entity<Delivery>().HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
  modelBuilder.Entity<Delivery>().HasOne(x => x.Alert).WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
  modelBuilder.Entity<Device>().HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
 }
}
public sealed class Organisation { public int Id { get; set; } public required string Name { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; }
public sealed class Administrator { public int Id { get; set; } public required string Email { get; set; } public required string PasswordHash { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; }
public sealed class Device
{
 public int Id { get; set; }
 public required string InstallationId { get; set; }
 public required string Name { get; set; }
 public required string CredentialHash { get; set; }
 public string? ProtectedCredential { get; set; }
 public string Status { get; set; } = "Approved";
 public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
 public DateTimeOffset? LastSeenAt { get; set; }
 public DateTimeOffset? RevokedAt { get; set; }
 public int? GroupId { get; set; }
 public DeviceGroup? Group { get; set; }
}
public sealed class JoinRequest
{
 public int Id { get; set; }
 public required string InstallationId { get; set; }
 public required string DeviceName { get; set; }
 public required string ClaimHash { get; set; }
 public string Status { get; set; } = "Pending";
 public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
 public DateTimeOffset? DecidedAt { get; set; }
 public int? DecidedById { get; set; }
 public int? DeviceId { get; set; }
}
public sealed class DeviceGroup { public int Id { get; set; } public required string Name { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; }
public sealed class Alert
{
 public int Id { get; set; }
 public required string Title { get; set; }
 public required string Message { get; set; }
 public required string Severity { get; set; }
 public bool SpeakMessage { get; set; }
 public required string Audience { get; set; }
 public int? GroupId { get; set; }
 public int? DeviceId { get; set; }
 public int CreatedById { get; set; }
 public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
 public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class Delivery
{
 public int Id { get; set; }
 public int AlertId { get; set; }
 public Alert Alert { get; set; } = null!;
 public int DeviceId { get; set; }
 public Device Device { get; set; } = null!;
 public string Status { get; set; } = "Queued";
 public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
 public DateTimeOffset? AcknowledgedAt { get; set; }
 public DateTimeOffset? ResolvedAt { get; set; }
}
public sealed class AuditEntry
{
 public int Id { get; set; }
 public int? AdministratorId { get; set; }
 public int? DeviceId { get; set; }
 public required string Action { get; set; }
 public required string Details { get; set; }
 public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

