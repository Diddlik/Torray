using System.Security.Cryptography;
using System.Text;
using Torray.Domain;

namespace Torray.Infrastructure;

public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    private static readonly byte[] Entropy = "Torray.SecretStore.v1"u8.ToArray();

    public async Task<string?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser));
    }

    public async Task SetAsync(Guid id, string secret, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);

        var path = PathFor(id);
        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, protectedBytes, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    public void Delete(Guid id) => File.Delete(PathFor(id));

    private string PathFor(Guid id) => Path.Combine(directory, id.ToString("N") + ".bin");
}
