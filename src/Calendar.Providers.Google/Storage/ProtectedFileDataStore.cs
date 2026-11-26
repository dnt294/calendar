using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace Calendar.Providers.Google.Storage;

/// <summary>Encrypts/decrypts token bytes before they touch the disk.</summary>
public interface IDataProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>Windows DPAPI, scoped to the current Windows user.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiDataProtector : IDataProtector
{
    private static readonly byte[] Entropy = "Calendar.Providers.Google.tokens.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// Google <see cref="IDataStore"/> that keeps one encrypted file per key, replacing the
/// plaintext <c>FileDataStore</c>. File names are hashes, so keys (emails) are not visible on disk.
/// </summary>
public sealed class ProtectedFileDataStore : IDataStore
{
    private readonly string _folder;
    private readonly IDataProtector _protector;

    public ProtectedFileDataStore(string folder, IDataProtector protector)
    {
        _folder = folder;
        _protector = protector;
        Directory.CreateDirectory(folder);
    }

    public async Task StoreAsync<T>(string key, T value)
    {
        var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
        var encrypted = _protector.Protect(Encoding.UTF8.GetBytes(json));
        var path = PathFor<T>(key);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, encrypted);
        File.Move(temp, path, overwrite: true);
    }

    public Task DeleteAsync<T>(string key)
    {
        File.Delete(PathFor<T>(key));
        return Task.CompletedTask;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = PathFor<T>(key);
        if (!File.Exists(path))
            return default!;
        var json = Encoding.UTF8.GetString(_protector.Unprotect(await File.ReadAllBytesAsync(path)));
        return NewtonsoftJsonSerializer.Instance.Deserialize<T>(json);
    }

    public Task ClearAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_folder, "*.bin"))
            File.Delete(file);
        return Task.CompletedTask;
    }

    private string PathFor<T>(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{typeof(T).FullName}-{key}"));
        return Path.Combine(_folder, Convert.ToHexStringLower(hash) + ".bin");
    }
}
