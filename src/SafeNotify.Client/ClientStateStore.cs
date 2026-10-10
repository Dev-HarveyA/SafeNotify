using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SafeNotify.Client;

internal sealed class ClientStateStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SafeNotify.DeviceState.v1");
    private readonly string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SafeNotify", "device.dat");

    public ClientState Load()
    {
        try
        {
            if (!File.Exists(statePath)) return new ClientState();
            var protectedBytes = File.ReadAllBytes(statePath);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<ClientState>(clearBytes) ?? new ClientState();
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or JsonException)
        {
            return new ClientState();
        }
    }

    public bool Save(ClientState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
            File.WriteAllBytes(statePath, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
