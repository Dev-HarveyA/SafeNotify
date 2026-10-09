using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using SafeNotify.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using SafeNotify.Server.Data;

var contentRoot = ResolveContentRoot();
if (args.Contains("--migrate-data-only", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"SafeNotify server data is ready at {ServerDataStorage.Resolve(null, contentRoot)}");
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
var dataDirectory = ServerDataStorage.Resolve(builder.Configuration["SafeNotify:DataDirectory"], builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
builder.Logging.AddProvider(new SafeNotifyFileLoggerProvider(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SafeNotify", "Logs")));
var bindAddress = builder.Configuration["SafeNotify:BindAddress"] ?? "127.0.0.1";
var port = int.TryParse(builder.Configuration["SafeNotify:Port"], out var configuredPort) ? configuredPort : 5080;
var certificatePath = builder.Configuration["SafeNotify:CertificatePath"];
var certificateThumbprint = builder.Configuration["SafeNotify:CertificateThumbprint"];
var certificatePassword = Environment.GetEnvironmentVariable("SAFENOTIFY_CERT_PASSWORD");
var certificate = LoadCertificate(certificateThumbprint);
var cookieSecurePolicy = certificate is null && string.IsNullOrWhiteSpace(certificatePath) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
var bindIp = bindAddress == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(bindAddress);
if (!IPAddress.IsLoopback(bindIp) && certificate is null && string.IsNullOrWhiteSpace(certificatePath)) throw new InvalidOperationException("A TLS certificate is required when binding outside loopback.");
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
builder.Services.AddDbContext<SafeNotifyDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "safenotify.db")};Cache=Shared;Foreign Keys=True"));
var dataProtection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("SafeNotify");
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.AddScoped<IPasswordHasher<Administrator>, PasswordHasher<Administrator>>();
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-SafeNotify-CSRF"; options.Cookie.Name = "SafeNotify.Csrf"; options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = cookieSecurePolicy; options.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
 options.Cookie.Name = "SafeNotify.Session";
 options.Cookie.HttpOnly = true;
 options.Cookie.SecurePolicy = cookieSecurePolicy;
 options.Cookie.SameSite = SameSiteMode.Strict;
 options.SlidingExpiration = true;
 options.ExpireTimeSpan = TimeSpan.FromHours(8);
 options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
 options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
 options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
 options.AddPolicy("public", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<DevicePresence>();
builder.Services.AddHostedService<AlertExpiryService>();
builder.Services.AddProblemDetails();
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<SafeNotifyDbContext>().Database.EnsureCreatedAsync();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
 context.Response.Headers.XContentTypeOptions = "nosniff";
 context.Response.Headers.XFrameOptions = "DENY";
 context.Response.Headers["Referrer-Policy"] = "no-referrer";
 context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'";
 await next();
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
 var path = context.Request.Path;
 var mutating = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
 if (mutating && (path.StartsWithSegments("/api/admin") || path.Equals("/api/auth/logout")) && context.User.Identity?.IsAuthenticated == true)
 {
  try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
  catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Invalid request token." }); return; }
 }
 await next();
});
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapCoreEndpoints();
app.MapAdminEndpoints();
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
    var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint.Replace(" ", string.Empty), false);
    var certificate = matches.Cast<X509Certificate2>().FirstOrDefault(candidate => candidate.HasPrivateKey);
    if (certificate is null)
    {
        throw new InvalidOperationException("The configured TLS certificate was not found with its private key in the current user's Personal certificate store.");
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

public partial class Program { }





