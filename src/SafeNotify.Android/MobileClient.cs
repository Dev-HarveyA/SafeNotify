using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace SafeNotify.Android;

public sealed class MobileClientState
{
    public string? ServerUrl { get; set; }
    public string? InstallationId { get; set; }
    public string? ClaimToken { get; set; }
    public int? RequestId { get; set; }
    public string? Credential { get; set; }
    public int? DeviceId { get; set; }
}

public sealed record MobileDiscovery(string Organisation, string ServerId);
public sealed record MobileJoinInput(string InstallationId, string DeviceName, string ClaimToken);
public sealed record MobileJoinResult(int RequestId, string Status);
public sealed record MobileClaimInput(string ClaimToken);
public sealed record MobileJoinStatus(string Status, string? Credential, int? DeviceId);
public sealed record MobileAlert(
    int Id,
    int DeliveryId,
    int AlertId,
    string Title,
    string Message,
    string Severity,
    DateTimeOffset ExpiresAt,
    bool SpeakMessage = false);

public sealed class MobileClient : IDisposable
{
    private const string StateKey = "SafeNotify.Mobile.State";
    private const string RootKey = "SafeNotify.Mobile.Root";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MobileClientState state;
    private readonly HttpClient http;

    public MobileClient(MobileClientState state, string? rootCertificate)
    {
        this.state = state;
        var handler = new HttpClientHandler();
        var rootBytes = string.IsNullOrWhiteSpace(rootCertificate)
            ? null
            : Convert.FromBase64String(rootCertificate);
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            ValidateCertificate(certificate, errors, rootBytes);
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        if (Uri.TryCreate(state.ServerUrl, UriKind.Absolute, out var address)) http.BaseAddress = address;
    }

    public static async Task<MobileClientState> LoadStateAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(StateKey);
            if (json is null) return new MobileClientState();
            return JsonSerializer.Deserialize<MobileClientState>(json, JsonOptions)
                ?? new MobileClientState();
        }
        catch
        {
            return new MobileClientState();
        }
    }

    public static async Task<string?> LoadRootAsync()
    {
        try { return await SecureStorage.Default.GetAsync(RootKey); }
        catch { return null; }
    }

    public static async Task SaveRootAsync(string certificate)
    {
        await SecureStorage.Default.SetAsync(RootKey, certificate);
    }

    public async Task SaveAsync()
    {
        await SecureStorage.Default.SetAsync(StateKey, JsonSerializer.Serialize(state, JsonOptions));
    }

    public async Task<MobileDiscovery> DiscoverAsync()
    {
        using var response = await http.GetAsync("/api/discovery");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MobileDiscovery>(JsonOptions)
            ?? throw new InvalidOperationException("The server returned an empty organisation response.");
    }

    public async Task RequestAccessAsync(string deviceName)
    {
        state.InstallationId ??= Guid.NewGuid().ToString();
        state.ClaimToken ??= Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        var input = new MobileJoinInput(state.InstallationId, deviceName, state.ClaimToken);
        using var response = await http.PostAsJsonAsync("/api/client/join", input, JsonOptions);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MobileJoinResult>(JsonOptions)
            ?? throw new InvalidOperationException("The server returned an empty access request response.");
        state.RequestId = result.RequestId;
        await SaveAsync();
    }

    public async Task<bool> CheckApprovalAsync()
    {
        if (state.RequestId is null || string.IsNullOrWhiteSpace(state.ClaimToken)) return false;
        using var response = await http.PostAsJsonAsync(
            $"/api/client/join/{state.RequestId}/status",
            new MobileClaimInput(state.ClaimToken),
            JsonOptions);
        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<MobileJoinStatus>(JsonOptions);
        if (result?.Status == "Rejected")
        {
            state.RequestId = null;
            state.ClaimToken = null;
            await SaveAsync();
            return false;
        }
        if (result?.Status != "Approved" || string.IsNullOrWhiteSpace(result.Credential)) return false;
        state.Credential = result.Credential;
        state.DeviceId = result.DeviceId;
        state.RequestId = null;
        state.ClaimToken = null;
        await SaveAsync();
        return true;
    }

    public async Task<bool> CheckSessionAsync()
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/client/session");
        using var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            state.Credential = null;
            state.DeviceId = null;
            await SaveAsync();
            return false;
        }
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IReadOnlyList<MobileAlert>> GetAlertsAsync()
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/client/alerts");
        using var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            state.Credential = null;
            state.DeviceId = null;
            await SaveAsync();
            throw new UnauthorizedAccessException("This device is no longer approved.");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MobileAlert>>(JsonOptions) ?? [];
    }

    public Task<bool> MarkDeliveredAsync(int id) => PostAsync($"/api/client/deliveries/{id}/delivered");
    public Task<bool> AcknowledgeAsync(int id) => PostAsync($"/api/client/deliveries/{id}/acknowledge");

    public void Dispose() => http.Dispose();

    private async Task<bool> PostAsync(string path)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(state.Credential))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Credential);
        return request;
    }

    private static bool ValidateCertificate(X509Certificate2? certificate, System.Net.Security.SslPolicyErrors errors, byte[]? rootBytes)
    {
        if (certificate is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
        if (rootBytes is null) return errors == System.Net.Security.SslPolicyErrors.None;
        using var root = X509CertificateLoader.LoadCertificate(rootBytes);
        if (!root.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority)) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(
            new Oid("1.3.6.1.5.5.7.3.1"));
        return chain.Build(certificate);
    }
}
