using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SafeNotify.Server;
using SafeNotify.Server.Data;

if (args.Contains("--safe-notify-restart-wait", StringComparer.OrdinalIgnoreCase))
{
    await Task.Delay(TimeSpan.FromSeconds(4));
    if (Environment.ProcessPath is { } restartExecutable)
    {
        var restart = new System.Diagnostics.ProcessStartInfo(restartExecutable)
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in args.Where(argument => !string.Equals(argument, "--safe-notify-restart-wait", StringComparison.OrdinalIgnoreCase)))
        {
            restart.ArgumentList.Add(argument);
        }
        System.Diagnostics.Process.Start(restart);
    }
    return;
}

var contentRoot = ResolveContentRoot();
if (args.Contains("--migrate-data-only", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"SafeNotify server data is ready at {ServerDataStorage.Resolve(null, contentRoot)}");
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
var networkSettingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SafeNotify",
    "Server",
    "network-settings.json");
builder.Configuration.AddJsonFile(networkSettingsPath, optional: true, reloadOnChange: true);
var dataDirectory = ServerDataStorage.Resolve(builder.Configuration["SafeNotify:DataDirectory"], builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
var logDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SafeNotify",
    "Logs");
builder.Logging.AddProvider(new SafeNotifyFileLoggerProvider(logDirectory));
var bindAddress = builder.Configuration["SafeNotify:BindAddress"] ?? "127.0.0.1";
var port = int.TryParse(builder.Configuration["SafeNotify:Port"], out var configuredPort) ? configuredPort : 5080;
var certificatePath = builder.Configuration["SafeNotify:CertificatePath"];
var certificateThumbprint = builder.Configuration["SafeNotify:CertificateThumbprint"];
var certificatePassword = Environment.GetEnvironmentVariable("SAFENOTIFY_CERT_PASSWORD");
var certificate = LoadCertificate(certificateThumbprint);
var cookieSecurePolicy = certificate is null && string.IsNullOrWhiteSpace(certificatePath) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
var bindIp = bindAddress == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(bindAddress);
if (!IPAddress.IsLoopback(bindIp) && certificate is null && string.IsNullOrWhiteSpace(certificatePath))
{
    throw new InvalidOperationException("A TLS certificate is required when binding outside loopback.");
}

builder.WebHost.ConfigureKestrel(options => options.Listen(bindIp, port, listen =>
{
    if (certificate is not null)
    {
        listen.UseHttps(certificate);
    }
    else if (!string.IsNullOrWhiteSpace(certificatePath))
    {
        listen.UseHttps(certificatePath, certificatePassword);
    }
}));
var databasePath = Path.Combine(dataDirectory, "safenotify.db");
builder.Services.AddDbContext<SafeNotifyDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath};Cache=Shared;Foreign Keys=True"));
var keyDirectory = new DirectoryInfo(Path.Combine(dataDirectory, "keys"));
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keyDirectory)
    .SetApplicationName("SafeNotify");
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.AddScoped<IPasswordHasher<Administrator>, PasswordHasher<Administrator>>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-SafeNotify-CSRF";
    options.Cookie.Name = "SafeNotify.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "SafeNotify.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<DevicePresence>();
builder.Services.AddHostedService<AlertExpiryService>();
builder.Services.AddProblemDetails();
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SafeNotifyDbContext>();
    await DatabaseMigrations.ApplyAsync(db);
}
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy = string.Join(
        "; ",
        "default-src 'self'",
        "script-src 'self'",
        "style-src 'self'",
        "connect-src 'self' ws: wss:",
        "img-src 'self' data:",
        "object-src 'none'",
        "frame-ancestors 'none'",
        "base-uri 'self'");
    await next();
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var mutating = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
    var isAdminRequest = path.StartsWithSegments("/api/admin");
    var isLogoutRequest = path.Equals("/api/auth/logout");
    if (mutating && (isAdminRequest || isLogoutRequest) && context.User.Identity?.IsAuthenticated == true)
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid request token." });
            return;
        }
    }
    await next();
});
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapCoreEndpoints();
app.MapAdminEndpoints();
var tlsConfigured = certificate is not null || !string.IsNullOrWhiteSpace(certificatePath);
app.MapLanNetworkEndpoints(networkSettingsPath, bindAddress, port, tlsConfigured);
app.MapClientEndpoints();
app.MapHub<DeviceHub>("/hub/device");
app.Run();





static X509Certificate2? LoadCertificate(string? thumbprint)
{
    if (string.IsNullOrWhiteSpace(thumbprint))
    {
        return null;
    }

    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadOnly);
    var normalizedThumbprint = thumbprint.Replace(" ", string.Empty);
    var matches = store.Certificates.Find(
        X509FindType.FindByThumbprint,
        normalizedThumbprint,
        validOnly: false);
    var certificate = matches.Cast<X509Certificate2>().FirstOrDefault(candidate => candidate.HasPrivateKey);
    if (certificate is null)
    {
        throw new InvalidOperationException(
            "The configured TLS certificate was not found with its private key in the current user's Personal certificate store.");
    }

    return certificate;
}

static string ResolveContentRoot()
{
    var executableDirectory = AppContext.BaseDirectory;
    if (Directory.Exists(Path.Combine(executableDirectory, "wwwroot")))
    {
        return executableDirectory;
    }

    var currentDirectory = Directory.GetCurrentDirectory();
    if (Directory.Exists(Path.Combine(currentDirectory, "wwwroot")))
    {
        return currentDirectory;
    }

    var projectDirectory = Path.Combine(currentDirectory, "src", "SafeNotify.Server");
    return Directory.Exists(Path.Combine(projectDirectory, "wwwroot")) ? projectDirectory : currentDirectory;
}

public partial class Program
{
}





