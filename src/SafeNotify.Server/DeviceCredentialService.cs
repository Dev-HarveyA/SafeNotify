using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SafeNotify.Server.Data;

namespace SafeNotify.Server;

public static class DeviceCredentialService
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static async Task<Device?> AuthenticateAsync(HttpContext context, SafeNotifyDbContext db)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[7..].Trim();
        if (token.Length < 32) return null;
        return await db.Devices.SingleOrDefaultAsync(x => x.CredentialHash == Hash(token) && x.Status == "Approved");
    }
}
