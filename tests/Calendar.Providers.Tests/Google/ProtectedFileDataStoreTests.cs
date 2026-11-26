using System.Text;
using Calendar.Providers.Google.Storage;
using Google.Apis.Auth.OAuth2.Responses;

namespace Calendar.Providers.Tests.Google;

public sealed class ProtectedFileDataStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"calendar-tokens-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static TokenResponse Token(string refresh) => new()
    {
        AccessToken = "access-" + refresh,
        RefreshToken = refresh,
        ExpiresInSeconds = 3600,
        IssuedUtc = new DateTime(2026, 11, 26, 1, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Round_trip_and_delete()
    {
        var store = new ProtectedFileDataStore(_folder, new XorProtector());

        await store.StoreAsync("me@gmail.com", Token("r1"));
        var loaded = await store.GetAsync<TokenResponse>("me@gmail.com");

        Assert.Equal("r1", loaded.RefreshToken);
        Assert.Equal("access-r1", loaded.AccessToken);

        await store.DeleteAsync<TokenResponse>("me@gmail.com");
        Assert.Null(await store.GetAsync<TokenResponse>("me@gmail.com"));
    }

    [Fact]
    public async Task Missing_key_returns_null()
    {
        var store = new ProtectedFileDataStore(_folder, new XorProtector());
        Assert.Null(await store.GetAsync<TokenResponse>("nobody"));
    }

    [Fact]
    public async Task Keys_are_isolated_and_overwrite_works()
    {
        var store = new ProtectedFileDataStore(_folder, new XorProtector());
        await store.StoreAsync("a@x.com", Token("a1"));
        await store.StoreAsync("b@x.com", Token("b1"));
        await store.StoreAsync("a@x.com", Token("a2"));

        Assert.Equal("a2", (await store.GetAsync<TokenResponse>("a@x.com")).RefreshToken);
        Assert.Equal("b1", (await store.GetAsync<TokenResponse>("b@x.com")).RefreshToken);

        await store.ClearAsync();
        Assert.Null(await store.GetAsync<TokenResponse>("b@x.com"));
    }

    [Fact]
    public async Task Nothing_readable_on_disk()
    {
        var store = new ProtectedFileDataStore(_folder, new XorProtector());
        await store.StoreAsync("me@gmail.com", Token("secret-refresh"));

        var file = Assert.Single(Directory.GetFiles(_folder));
        Assert.DoesNotContain("me@gmail.com", Path.GetFileName(file));
        var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
        Assert.DoesNotContain("secret-refresh", raw);
    }

    [Fact]
    public async Task Dpapi_round_trip_on_windows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new ProtectedFileDataStore(_folder, new DpapiDataProtector());
        await store.StoreAsync("me@gmail.com", Token("r"));
        Assert.Equal("r", (await store.GetAsync<TokenResponse>("me@gmail.com")).RefreshToken);
    }

    /// <summary>Deterministic stand-in for DPAPI.</summary>
    private sealed class XorProtector : IDataProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] data) => Protect(data);
    }
}
