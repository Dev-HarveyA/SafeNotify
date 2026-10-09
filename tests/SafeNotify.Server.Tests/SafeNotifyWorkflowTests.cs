using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;

namespace SafeNotify.Server.Tests;

public sealed class SafeNotifyWorkflowTests
{
    [Fact]
    public async Task ApprovalAlertAcknowledgementAndRevocationFlowWorks()
    {
        using var factory = new SafeNotifyFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var unauthorized = await client.GetAsync("/api/admin/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var setup = await client.PostAsJsonAsync("/api/setup", new { organisation = "Northwind School", email = "admin@example.org", password = "a-very-long-password-9876" });
        var setupBody = await setup.Content.ReadAsStringAsync();
        Assert.True(setup.StatusCode == HttpStatusCode.OK, $"Setup failed: {(int)setup.StatusCode} {setupBody}");
        var csrfResponse = await client.GetFromJsonAsync<JsonElement>("/api/security/csrf");
        client.DefaultRequestHeaders.Add("X-SafeNotify-CSRF", csrfResponse.GetProperty("token").GetString());

        var groupResponse = await client.PostAsJsonAsync("/api/admin/groups", new { name = "Reception" });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var group = await groupResponse.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = group.GetProperty("id").GetInt32();

        var installationId = Guid.NewGuid().ToString();
        var claimToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var joinResponse = await client.PostAsJsonAsync("/api/client/join", new { installationId, deviceName = "Reception PC", claimToken });
        var joinBody = await joinResponse.Content.ReadAsStringAsync();
        Assert.True(joinResponse.StatusCode == HttpStatusCode.Accepted, $"Join request failed: {(int)joinResponse.StatusCode} {joinBody}");
        var join = await joinResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = join.GetProperty("requestId").GetInt32();

        var requests = await client.GetFromJsonAsync<JsonElement[]>("/api/admin/requests");
        Assert.NotNull(requests);
        Assert.Single(requests);
        var approval = await client.PostAsync($"/api/admin/requests/{requestId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        var claim = await client.PostAsJsonAsync($"/api/client/join/{requestId}/status", new { claimToken });
        var credentialResponse = await claim.Content.ReadFromJsonAsync<JsonElement>();
        var credential = credentialResponse.GetProperty("credential").GetString();
        Assert.False(string.IsNullOrWhiteSpace(credential));

        var assignment = await client.PutAsJsonAsync("/api/admin/devices/1/group", new { groupId });
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);

        var alert = await client.PostAsJsonAsync("/api/admin/alerts", new { title = "Drill notice", message = "Proceed to the assembly point.", severity = "emergency", audience = "All", groupId = (int?)null, deviceId = (int?)null, expiresMinutes = 60 });
        Assert.Equal(HttpStatusCode.Created, alert.StatusCode);
        var alertRecord = await alert.Content.ReadFromJsonAsync<JsonElement>();
        var alertId = alertRecord.GetProperty("id").GetInt32();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        var deliveries = await client.GetFromJsonAsync<JsonElement[]>("/api/client/alerts");
        Assert.NotNull(deliveries);
        Assert.Single(deliveries);
        var deliveryId = deliveries[0].GetProperty("id").GetInt32();
        var acknowledgement = await client.PostAsync($"/api/client/deliveries/{deliveryId}/acknowledge", null);
        Assert.Equal(HttpStatusCode.OK, acknowledgement.StatusCode);
        var resolution = await client.PostAsync($"/api/admin/alerts/{alertId}/resolve", null);
        Assert.Equal(HttpStatusCode.OK, resolution.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var history = await client.GetFromJsonAsync<JsonElement[]>("/api/admin/alerts");
        Assert.Equal(1, history![0].GetProperty("resolved").GetInt32());

        client.DefaultRequestHeaders.Authorization = null;
        var revoke = await client.PostAsync("/api/admin/devices/1/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        var revokedAccess = await client.GetAsync("/api/client/alerts");
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAccess.StatusCode);
    }
}

public sealed class SafeNotifyFactory : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), $"safenotify-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("SafeNotify:DataDirectory", dataDirectory);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SafeNotify:DataDirectory"] = dataDirectory,
            ["SafeNotify:BindAddress"] = "127.0.0.1",
            ["SafeNotify:Port"] = "0"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (disposing && Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
    }
}









