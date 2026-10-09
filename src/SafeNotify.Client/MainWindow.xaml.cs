using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.AspNetCore.SignalR.Client;
using Color = System.Windows.Media.Color;

namespace SafeNotify.Client;

public partial class MainWindow : Window
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SafeNotify.DeviceState.v1");
    private readonly HashSet<int> visibleDeliveries = [];
    private readonly Queue<IncomingAlert> alertQueue = [];
    private readonly string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SafeNotify", "device.dat");
    private ClientState state = new();
    private AlertWindowGroup? activeAlertWindowGroup;
    private HubConnection? hub;
    private CancellationTokenSource? connectionLoopCancellation;
    private Task? connectionLoop;
    private bool requestPending;
    private bool shuttingDown;
    private CancellationTokenSource? polling;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
        Closing += Window_Closing;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        state = LoadState();
        if (!string.IsNullOrWhiteSpace(state.ServerUrl)) ServerAddress.Text = state.ServerUrl;
        if (!string.IsNullOrWhiteSpace(state.Credential))
        {
            StartConnectionLoop();
            return;
        }
        if (state.RequestId is not null && !string.IsNullOrWhiteSpace(state.ClaimToken))
        {
            requestPending = true;
            SetStatus("Your request has been sent. Waiting for administrator approval.", Colors.Goldenrod);
            StartPolling();
            await PollApprovalAsync();
            return;
        }
        await DiscoverAsync();
    }

    private async void Discover_Click(object sender, RoutedEventArgs e) => await DiscoverAsync();

    private async Task DiscoverAsync()
    {
        if (!TryGetBaseAddress(out var address))
        {
            SetStatus("Enter a valid HTTPS server address. HTTP is allowed only on this computer for development.", Colors.OrangeRed);
            return;
        }
        DiscoverButton.IsEnabled = false;
        state.ServerUrl = address.ToString().TrimEnd('/');
        SaveState();
        if (!string.IsNullOrWhiteSpace(state.Credential))
        {
            connectionLoopCancellation?.Cancel();
            if (hub is not null)
            {
                await hub.DisposeAsync();
                hub = null;
            }
            connectionLoop = null;
            connectionLoopCancellation?.Dispose();
            connectionLoopCancellation = null;
            StartConnectionLoop();
        }
        SetStatus("Looking for your organisation system…", Colors.Goldenrod);
        try
        {
            using var response = await Client.GetAsync(new Uri(address, "/api/discovery"));
            if (!response.IsSuccessStatusCode)
            {
                OrganisationName.Text = "Organisation system not detected.";
                SetStatus("Organisation system not detected. Check the address or contact your administrator.", Colors.OrangeRed);
                JoinButton.IsEnabled = false;
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<DiscoveryResponse>();
            state.ServerUrl = address.ToString().TrimEnd('/');
            SaveState();
            OrganisationName.Text = result?.Organisation ?? "Organisation system found.";
            SetStatus("Organisation system found. You can request access.", Colors.SeaGreen);
            JoinButton.IsEnabled = !requestPending && string.IsNullOrWhiteSpace(state.Credential);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            OrganisationName.Text = "Organisation system not detected.";
            SetStatus("Could not reach that server. Check the address and your network connection.", Colors.OrangeRed);
            JoinButton.IsEnabled = false;
        }
        finally { DiscoverButton.IsEnabled = true; }
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetBaseAddress(out var address)) return;
        state.ServerUrl = address.ToString().TrimEnd('/');
        state.InstallationId ??= Guid.NewGuid().ToString();
        state.ClaimToken ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        SaveState();
        JoinButton.IsEnabled = false;
        try
        {
            using var response = await Client.PostAsJsonAsync(new Uri(address, "/api/client/join"), new JoinInput(state.InstallationId, Environment.MachineName, state.ClaimToken));
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                SetStatus(error?.Error ?? "The join request could not be sent. Check the server and try again.", Colors.OrangeRed);
                JoinButton.IsEnabled = true;
                return;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                SetStatus(error?.Error ?? "This installation cannot submit another request.", Colors.OrangeRed);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<JoinResponse>();
            state.RequestId = result?.RequestId;
            requestPending = true;
            SaveState();
            SetStatus("Your request has been sent. Waiting for administrator approval.", Colors.Goldenrod);
            StartPolling();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            SetStatus("The server could not be reached. Your request was not sent.", Colors.OrangeRed);
            JoinButton.IsEnabled = true;
        }
    }

    private void StartPolling()
    {
        polling?.Cancel();
        polling = new CancellationTokenSource();
        var token = polling.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && requestPending)
            {
                await PollApprovalAsync();
                try { await Task.Delay(TimeSpan.FromSeconds(15), token); }
                catch (OperationCanceledException) { return; }
            }
        }, token);
    }

    private async Task PollApprovalAsync()
    {
        if (state.RequestId is null || string.IsNullOrWhiteSpace(state.ClaimToken) || !TryGetBaseAddress(out var address)) return;
        try
        {
            var url = new Uri(address, $"/api/client/join/{state.RequestId}/status");
            using var response = await Client.PostAsJsonAsync(url, new ClaimStatusInput(state.ClaimToken));
            if (!response.IsSuccessStatusCode) return;
            var result = await response.Content.ReadFromJsonAsync<JoinStatusResponse>();
            if (result?.Status == "Approved" && !string.IsNullOrWhiteSpace(result.Credential))
            {
                state.Credential = result.Credential;
                state.DeviceId = result.DeviceId;
                state.RequestId = null;
                state.ClaimToken = null;
                requestPending = false;
                polling?.Cancel();
                SaveState();
                await Dispatcher.InvokeAsync(StartConnectionLoop);
            }
            else if (result?.Status == "Rejected")
            {
                requestPending = false;
                polling?.Cancel();
                state.RequestId = null;
                state.ClaimToken = null;
                SaveState();
                await Dispatcher.InvokeAsync(() => SetStatus("Your request was rejected. Contact your administrator if you need access.", Colors.OrangeRed));
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException) { }
    }

    private void StartConnectionLoop()
    {
        if (shuttingDown || string.IsNullOrWhiteSpace(state.Credential) || connectionLoop is { IsCompleted: false }) return;
        connectionLoopCancellation?.Dispose();
        connectionLoopCancellation = new CancellationTokenSource();
        connectionLoop = RunConnectionLoopAsync(connectionLoopCancellation.Token);
    }

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested && !shuttingDown && !string.IsNullOrWhiteSpace(state.Credential))
        {
            if (!TryGetBaseAddress(out var address))
            {
                SetStatus("Enter a valid server address to reconnect.", Colors.OrangeRed);
                return;
            }
            if (hub?.State == HubConnectionState.Connected)
            {
                await PollAlertsAsync(cancellationToken);
                try { await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                if (hub is not null)
                {
                    await hub.DisposeAsync();
                    hub = null;
                }
                var connection = new HubConnectionBuilder()
                    .WithUrl(new Uri(address, $"/hub/device?access_token={Uri.EscapeDataString(state.Credential!)}"))
                    .WithAutomaticReconnect(new PersistentReconnectPolicy())
                    .Build();
                hub = connection;
                connection.On<IncomingAlert>("AlertReceived", alert => Dispatcher.InvokeAsync(() => DisplayAlert(alert with { Id = alert.DeliveryId })).Task.Unwrap());
                connection.On("AccessRevoked", async () => await Dispatcher.InvokeAsync(async () =>
                {
                    state.Credential = null;
                    state.DeviceId = null;
                    SaveState();
                    SetStatus("Device access has been revoked. Contact your administrator.", Colors.OrangeRed);
                    JoinButton.IsEnabled = false;
                    connectionLoopCancellation?.Cancel();
                    await connection.StopAsync();
                }).Task.Unwrap());
                connection.Reconnecting += _ => Dispatcher.InvokeAsync(() => SetStatus("Connection interrupted. Reconnecting automatically…", Colors.Goldenrod)).Task;
                connection.Reconnected += async _ =>
                {
                    await Dispatcher.InvokeAsync(() => SetStatus("Connected and approved. You can receive organisation alerts.", Colors.SeaGreen));
                    await PollAlertsAsync(cancellationToken);
                };
                connection.Closed += _ => Dispatcher.InvokeAsync(() => SetStatus("Server offline. SafeNotify is retrying automatically…", Colors.OrangeRed)).Task;
                await connection.StartAsync(cancellationToken);
                retryDelay = TimeSpan.FromSeconds(2);
                JoinButton.IsEnabled = false;
                SetStatus("Connected and approved. You can receive organisation alerts.", Colors.SeaGreen);
                await PollAlertsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                SetStatus("Server offline. SafeNotify is retrying automatically…", Colors.OrangeRed);
                if (hub is not null)
                {
                    await hub.DisposeAsync();
                    hub = null;
                }
                try { await Task.Delay(retryDelay, cancellationToken); }
                catch (OperationCanceledException) { return; }
                retryDelay = TimeSpan.FromSeconds(Math.Min(30, retryDelay.TotalSeconds * 2));
            }
        }
    }

    private async Task PollAlertsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state.Credential) || !TryGetBaseAddress(out var address)) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "/api/client/alerts"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Credential);
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return;
            var alerts = await response.Content.ReadFromJsonAsync<List<IncomingAlert>>();
            if (alerts is null) return;
            foreach (var alert in alerts) await DisplayAlert(alert);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
    }
    private int activeDeliveryId;
    private bool isEmergency;

    private async Task DisplayAlert(IncomingAlert alert)
    {
        if (alert.ExpiresAt <= DateTimeOffset.UtcNow || !visibleDeliveries.Add(alert.Id)) return;
        alertQueue.Enqueue(alert);
        if (activeDeliveryId == 0) await ShowNextAlertAsync();
    }

    private async Task ShowNextAlertAsync()
    {
        if (activeDeliveryId != 0 || alertQueue.Count == 0) return;
        var alert = alertQueue.Dequeue();
        activeDeliveryId = alert.Id;
        isEmergency = string.Equals(alert.Severity, "emergency", StringComparison.OrdinalIgnoreCase);
        var popupGroup = AlertWindowGroup.ShowOnEveryScreen(alert.Title, alert.Message, alert.Severity, alert.SpeakMessage, AcknowledgeAlertAsync);
        activeAlertWindowGroup = popupGroup;
        popupGroup.AcknowledgedAndClosed += async () =>
        {
            if (!ReferenceEquals(activeAlertWindowGroup, popupGroup)) return;
            activeAlertWindowGroup = null;
            if (!shuttingDown && alertQueue.Count > 0) await ShowNextAlertAsync();
        };
        await PostAsync($"/api/client/deliveries/{alert.Id}/delivered");
    }

    private async Task<bool> AcknowledgeAlertAsync()
    {
        if (activeDeliveryId == 0) return false;
        if (!await PostAsync($"/api/client/deliveries/{activeDeliveryId}/acknowledge"))
        {
            SetStatus("Could not record acknowledgement. Check your connection and try again.", Colors.OrangeRed);
            return false;
        }
        if (isEmergency) SetStatus("Receipt acknowledged. This does not confirm the emergency is resolved.", Colors.SeaGreen);
        activeDeliveryId = 0;
        isEmergency = false;
        return true;
    }

    private async Task<bool> PostAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(state.Credential) || !TryGetBaseAddress(out var address)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(address, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Credential);
            using var response = await Client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException) { return false; }
    }

    private bool TryGetBaseAddress(out Uri address)
    {
        var text = ServerAddress.Text.Trim().TrimEnd('/');
        if (Uri.TryCreate(text, UriKind.Absolute, out var candidate) && candidate.AbsolutePath == "/" && candidate.Query.Length == 0 && candidate.Fragment.Length == 0 && (candidate.Scheme == Uri.UriSchemeHttps || candidate.Scheme == Uri.UriSchemeHttp && candidate.IsLoopback))
        {
            address = candidate;
            return true;
        }
        address = new Uri("https://localhost");
        return false;
    }

    private void SetStatus(string text, Color color)
    {
        StatusMessage.Text = text;
        ConnectionStatus.Text = text.Length > 45 ? text[..42] + "…" : text;
        ConnectionDot.Fill = new SolidColorBrush(color);
    }

    private ClientState LoadState()
    {
        try
        {
            if (!File.Exists(statePath)) return new ClientState();
            var protectedBytes = File.ReadAllBytes(statePath);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var loaded = JsonSerializer.Deserialize<ClientState>(clearBytes) ?? new ClientState();
            ServerAddress.Text = loaded.ServerUrl ?? ServerAddress.Text;
            return loaded;
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or JsonException) { return new ClientState(); }
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
            File.WriteAllBytes(statePath, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or UnauthorizedAccessException)
        {
            SetStatus("Could not securely save this device's local registration.", Colors.OrangeRed);
        }
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        shuttingDown = true;
        polling?.Cancel();
        connectionLoopCancellation?.Cancel();
        activeAlertWindowGroup?.CloseForShutdown();
        if (hub is not null) await hub.DisposeAsync();
        connectionLoopCancellation?.Dispose();
    }

    private sealed class ClientState
    {
        public string? ServerUrl { get; set; }
        public string? InstallationId { get; set; }
        public string? ClaimToken { get; set; }
        public int? RequestId { get; set; }
        public string? Credential { get; set; }
        public int? DeviceId { get; set; }
    }

    private sealed record DiscoveryResponse(string Organisation, string ServerId);
    private sealed record JoinInput(string InstallationId, string DeviceName, string ClaimToken);
    private sealed record JoinResponse(int RequestId, string Status);
    private sealed record JoinStatusResponse(string Status, string? Credential, int? DeviceId);
    private sealed record ClaimStatusInput(string ClaimToken);
    private sealed record IncomingAlert(int Id, int DeliveryId, int AlertId, string Title, string Message, string Severity, DateTimeOffset ExpiresAt, bool SpeakMessage = false);
    private sealed record ErrorResponse(string Error);

    private sealed class PersistentReconnectPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext context) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(context.PreviousRetryCount, 6))));
    }
}





