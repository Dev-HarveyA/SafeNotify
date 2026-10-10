using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SafeNotify.Server.Data;

namespace SafeNotify.Server;

public static class CoreEndpoints
{
    public static void MapCoreEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", async (SafeNotifyDbContext db) => Results.Ok(new { status = "online", configured = await db.Organisations.AnyAsync(), time = DateTimeOffset.UtcNow }));
        app.MapGet("/api/discovery", async (SafeNotifyDbContext db) =>
        {
            var org = await db.Organisations.AsNoTracking().FirstOrDefaultAsync();
            return org is null ? Results.NotFound(new { message = "Organisation system not detected." }) : Results.Ok(new { organisation = org.Name, serverId = "safenotify-local" });
        });
        app.MapGet("/api/setup/status", async (SafeNotifyDbContext db) => Results.Ok(new { complete = await db.Administrators.AnyAsync() }));
        app.MapPost("/api/setup", async (SetupRequest request, SafeNotifyDbContext db, IPasswordHasher<Administrator> hasher, HttpContext context) =>
        {
            if (await db.Administrators.AnyAsync()) return Results.Conflict(new { error = "Initial setup is already complete." });
            var email = request.Email.Trim().ToLowerInvariant();
            var name = request.Organisation.Trim();
            if (!IsEmail(email) || name.Length is < 2 or > 120 || request.Password.Length < 14) return Results.BadRequest(new { error = "Enter a valid email, organisation name, and password of at least 14 characters." });
            var admin = new Administrator { Email = email, PasswordHash = "pending" };
            admin.PasswordHash = hasher.HashPassword(admin, request.Password);
            db.Organisations.Add(new Organisation { Name = name });
            db.Administrators.Add(admin);
            await db.SaveChangesAsync();
            await SignIn(context, admin);
            db.AuditEntries.Add(new AuditEntry { AdministratorId = admin.Id, Action = "SetupCompleted", Details = "Initial administrator account created." });
            await db.SaveChangesAsync();
            return Results.Ok(new { email = admin.Email });
        }).RequireRateLimiting("public");
        app.MapPost("/api/auth/login", async (LoginRequest request, SafeNotifyDbContext db, IPasswordHasher<Administrator> hasher, HttpContext context) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Administrators.SingleOrDefaultAsync(x => x.Email == email);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
            await SignIn(context, user);
            db.AuditEntries.Add(new AuditEntry { AdministratorId = user.Id, Action = "Login", Details = "Administrator signed in." });
            await db.SaveChangesAsync();
            return Results.Ok(new { email = user.Email });
        }).RequireRateLimiting("public");
        app.MapPost("/api/auth/logout", async (HttpContext context) => { await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.Ok(new { signedOut = true }); });
        app.MapGet("/api/auth/me", (HttpContext context) => context.User.Identity?.IsAuthenticated == true ? Results.Ok(new { email = context.User.Identity.Name }) : Results.Unauthorized());
        app.MapGet("/api/security/csrf", (IAntiforgery antiforgery, HttpContext context) => { var token = antiforgery.GetAndStoreTokens(context); return Results.Ok(new { token = token.RequestToken }); });
    }
    private static async Task SignIn(HttpContext context, Administrator admin)
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, admin.Email), new System.Security.Claims.Claim("adminId", admin.Id.ToString()), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Administrator") };
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    }
    private static bool IsEmail(string value) => value.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value;
}
public sealed record SetupRequest(string Organisation, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);





