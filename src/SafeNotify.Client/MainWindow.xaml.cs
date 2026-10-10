using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Media;
using Microsoft.AspNetCore.SignalR.Client;
using Color = System.Windows.Media.Color;

namespace SafeNotify.Client;

public partial class MainWindow : Window
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HashSet<int> visibleDeliveries = [];
    private readonly Queue<IncomingAlert> alertQueue = [];
    private readonly ClientStateStore stateStore = new();
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
            await DiscoverAsync();
            return;
        }
        if (state.RequestId is not null && !string.IsNullOrWhiteSpace(state.ClaimToken))
        {
            requestPending = true;
            SetStatus("Access request sent. Waiting for your administrator to approve this device.", Colors.Goldenrod);
            StartPolling();
            await PollApprovalAsync();
            return;
        }
        await DiscoverAsync();
    }

    private async void Discover_Click(object sender, RoutedEventArgs e) => await DiscoverAsync();

    private void Window_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        var certificatePath = files.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".cer", StringComparison.OrdinalIgnoreCase));
        if (certificatePath is null)
        {
            SetStatus("Drop the public certificate supplied by your SafeNotify administrator.", Colors.OrangeRed);
            return;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            if (!certificate.Subject.Contains("SafeNotify LAN Root", StringComparison.OrdinalIgnoreCase)
                || !certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority))
            {
                SetStatus("That file is not a SafeNotify server certificate.", Colors.OrangeRed);
                return;
            }

            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
            if (existing.Count == 0) store.Add(certificate);
            SetStatus("Certificate installed for this Windows account. Checking the server again.", Colors.SeaGreen);
            await DiscoverAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            SetStatus("Certificate could not be installed. Check that you selected the public SafeNotify certificate.", Colors.OrangeRed);
        }
    }

    private async Task DiscoverAsync()
    {
        if (!TryGetBaseAddress(out var address))
        {
            SetStatus("Enter a valid HTTPS address. HTTP works only for a server on this computer.", Colors.OrangeRed);
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
        SetStatus("Checking the server...", Colors.Goldenrod);
        try
        {
            using var response = await Client.GetAsync(new Uri(address, "/api/discovery"));
            if (!response.IsSuccessStatusCode)
            {
                OrganisationName.Text = "Server not found.";
                SetStatus("Server not found. Check the address or ask your administrator.", Colors.OrangeRed);
                JoinButton.IsEnabled = false;
                return;
            }
            if (!string.IsNullOrWhiteSpace(state.Credential))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "/api/client/session"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Credential);
                using var sessionResponse = await Client.SendAsync(request);
                if (sessionResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await ClearInvalidCredentialAsync();
                }
                else if (!sessionResponse.IsSuccessStatusCode)
                {
                    SetStatus("The server responded, but SafeNotify could not check this device's approval. Try again.", Colors.OrangeRed);
                    JoinButton.IsEnabled = false;
                    return;
                }
            }
            var result = await response.Content.ReadFromJsonAsync<DiscoveryResponse>();
            state.ServerUrl = address.ToString().TrimEnd('/');
            SaveState();
            OrganisationName.Text = result?.Organisation ?? "Organisation found.";
            SetStatus("Organisation found. Select Request to join to ask for access.", Colors.SeaGreen);
            JoinButton.IsEnabled = !requestPending && string.IsNullOrWhiteSpace(state.Credential);
            if (string.IsNullOrWhiteSpace(state.Credential) && !requestPending)
                SetStatus("Organisation found. Select Request to join to ask for access.", Colors.SeaGreen);
            else if (!string.IsNullOrWhiteSpace(state.Credential))
                SetStatus("Connected. This device is approved to receive alerts.", Colors.SeaGreen);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            OrganisationName.Text = "Server not found.";
            SetStatus("Could not reach the server. Check the address and your network connection.", Colors.OrangeRed);
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
            var request = new JoinInput(state.InstallationId, Environment.MachineName, state.ClaimToken);
            using var response = await Client.PostAsJsonAsync(new Uri(address, "/api/client/join"), request);
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                SetStatus(error?.Error ?? "Could not send the access request. Check the server and try again.", Colors.OrangeRed);
                JoinButton.IsEnabled = true;
                return;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                SetStatus(error?.Error ?? "This device cannot submit another request right now.", Colors.OrangeRed);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<JoinResponse>();
            state.RequestId = result?.RequestId;
            requestPending = true;
            SaveState();
            SetStatus("Access request sent. Waiting for your administrator to approve this device.", Colors.Goldenrod);
            StartPolling();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            SetStatus("Could not reach the server. Your request was not sent.", Colors.OrangeRed);
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
                await Dispatcher.InvokeAsync(() =>
                    SetStatus("Access request declined. Contact your administrator if you still need access.", Colors.OrangeRed));
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
        }
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
                connection.On<IncomingAlert>(
                    "AlertReceived",
                    alert => Dispatcher.InvokeAsync(() => DisplayAlert(alert with { Id = alert.DeliveryId })).Task.Unwrap());
                connection.On("AccessRevoked", async () => await Dispatcher.InvokeAsync(async () =>
                {
                    await ClearInvalidCredentialAsync();
                    connectionLoopCancellation?.Cancel();
                    await connection.StopAsync();
                }).Task.Unwrap());
                connection.Reconnecting += _ => Dispatcher.InvokeAsync(() =>
                    SetStatus("Connection interrupted. Reconnecting now.", Colors.Goldenrod)).Task;
                connection.Reconnected += async _ =>
                {
                    await Dispatcher.InvokeAsync(() => SetStatus("Connected. This device is approved to receive alerts.", Colors.SeaGreen));
                    await PollAlertsAsync(cancellationToken);
                };
                connection.Closed += _ => Dispatcher.InvokeAsync(() =>
                    SetStatus("Server unavailable. SafeNotify will reconnect automatically.", Colors.OrangeRed)).Task;
                await connection.StartAsync(cancellationToken);
                retryDelay = TimeSpan.FromSeconds(2);
                JoinButton.IsEnabled = false;
                SetStatus("Connected. This device is approved to receive alerts.", Colors.SeaGreen);
                await PollAlertsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                SetStatus("Server unavailable. SafeNotify will reconnect automatically.", Colors.OrangeRed);
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
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await Dispatcher.InvokeAsync(ClearInvalidCredentialAsync);
                connectionLoopCancellation?.Cancel();
                return;
            }
            if (!response.IsSuccessStatusCode) return;
            var alerts = await response.Content.ReadFromJsonAsync<List<IncomingAlert>>();
            if (alerts is null) return;
            foreach (var alert in alerts) await DisplayAlert(alert);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
    }

    private Task ClearInvalidCredentialAsync()
    {
        state.Credential = null;
        state.DeviceId = null;
        SaveState();
        requestPending = false;
        state.RequestId = null;
        state.ClaimToken = null;
        SaveState();
        JoinButton.IsEnabled = true;
        SetStatus("This device is no longer approved. Submit a new access request if you need alerts.", Colors.OrangeRed);
        return Task.CompletedTask;
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
            SetStatus("Acknowledgement could not be recorded. Check the connection and try again.", Colors.OrangeRed);
            return false;
        }
        if (isEmergency) SetStatus("Acknowledgement recorded. The emergency is not marked as resolved.", Colors.SeaGreen);
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
        if (Uri.TryCreate(text, UriKind.Absolute, out var candidate))
        {
            var isAllowedScheme = candidate.Scheme == Uri.UriSchemeHttps
                || candidate.Scheme == Uri.UriSchemeHttp && candidate.IsLoopback;
            if (candidate.AbsolutePath == "/"
                && candidate.Query.Length == 0
                && candidate.Fragment.Length == 0
                && isAllowedScheme)
            {
                address = candidate;
                return true;
            }
        }
        address = new Uri("https://localhost");
        return false;
    }

    private void SetStatus(string text, Color color)
    {
        StatusMessage.Text = text;
        var statusColor = color == Colors.SeaGreen
            ? Color.FromRgb(36, 115, 79)
            : color == Colors.Goldenrod
                ? Color.FromRgb(138, 87, 0)
                : color == Colors.OrangeRed
                    ? Color.FromRgb(169, 40, 50)
                    : color;
        var indicatorColor = color == Colors.SeaGreen
            ? Color.FromRgb(103, 196, 142)
            : color == Colors.Goldenrod
                ? Color.FromRgb(227, 162, 82)
                : color == Colors.OrangeRed
                    ? Color.FromRgb(233, 122, 114)
                    : color;
        ConnectionStatus.Text = color == Colors.SeaGreen
            ? "Connected"
            : color == Colors.Goldenrod
                ? "In progress"
                : color == Colors.OrangeRed
                    ? "Needs attention"
                    : "Not connected";
        StatusMessage.Foreground = new SolidColorBrush(statusColor);
        ConnectionDot.Fill = new SolidColorBrush(indicatorColor);
    }

    private ClientState LoadState() => stateStore.Load();

    private void SaveState()
    {
        if (!stateStore.Save(state)) SetStatus("Could not securely save this device's local registration.", Colors.OrangeRed);
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


}





