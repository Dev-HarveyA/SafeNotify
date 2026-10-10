using Microsoft.AspNetCore.SignalR.Client;

namespace SafeNotify.Client;

internal sealed class ClientState
{
    public string? ServerUrl { get; set; }
    public string? InstallationId { get; set; }
    public string? ClaimToken { get; set; }
    public int? RequestId { get; set; }
    public string? Credential { get; set; }
    public int? DeviceId { get; set; }
}

internal sealed record DiscoveryResponse(string Organisation, string ServerId);
internal sealed record JoinInput(string InstallationId, string DeviceName, string ClaimToken);
internal sealed record JoinResponse(int RequestId, string Status);
internal sealed record JoinStatusResponse(string Status, string? Credential, int? DeviceId);
internal sealed record ClaimStatusInput(string ClaimToken);
internal sealed record IncomingAlert(int Id, int DeliveryId, int AlertId, string Title, string Message, string Severity, DateTimeOffset ExpiresAt, bool SpeakMessage = false);
internal sealed record ErrorResponse(string Error);

internal sealed class PersistentReconnectPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext context) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(context.PreviousRetryCount, 6))));
}
