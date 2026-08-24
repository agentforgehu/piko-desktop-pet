using System.Security.Cryptography;
using Piko.Context.Events;
using Piko.Memory.Security;

namespace Piko.Memory.Tests;

public sealed class MemoryStoreTests
{
    [Fact]
    public void AesGcmProtectorBindsCiphertextToPurpose()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var protector = new AesGcmMemoryProtector(key);
        var encrypted = protector.Protect("private memory", "one");

        Assert.Equal("private memory", protector.Unprotect(encrypted, "one"));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(encrypted, "two"));
    }

    [Fact]
    public async Task StoreRoundTripsWithoutWritingMemoryPlaintextToDatabase()
    {
        var (root, path, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var added = await store.AddAsync(
                new MemoryDraft(
                    MemoryKind.Episodic,
                    "User fixed the build",
                    "No source code, only a result summary",
                    DataSensitivity.Medium,
                    "test"),
                now);

            var loaded = await store.GetAsync(added.Id, now);
            Assert.Equal(added, loaded);
            var databaseBytes = await File.ReadAllBytesAsync(path);
            var databaseText = System.Text.Encoding.UTF8.GetString(databaseBytes);
            Assert.DoesNotContain("User fixed the build", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("No source code", databaseText, StringComparison.Ordinal);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExpiryAndDeleteControlsAreEnforced()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.AddAsync(
                new MemoryDraft(
                    MemoryKind.Working,
                    "temporary",
                    "payload",
                    DataSensitivity.Low,
                    "test",
                    now.AddSeconds(1)),
                now);
            await store.AddAsync(
                new MemoryDraft(
                    MemoryKind.Profile,
                    "persistent preference",
                    "payload",
                    DataSensitivity.Medium,
                    "test"),
                now);

            Assert.Single(await store.ListAsync(now, MemoryKind.Working));
            Assert.Equal(1, await store.PurgeExpiredAsync(now.AddSeconds(2)));
            Assert.Empty(await store.ListAsync(now.AddSeconds(2), MemoryKind.Working));
            Assert.Equal(1, await store.DeleteAllAsync());
            Assert.Empty(await store.ListAsync(now.AddSeconds(2)));
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static (string Root, string Path, SqliteMemoryStore Store) CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "PikoMemoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "memory.db");
        var key = RandomNumberGenerator.GetBytes(32);
        return (root, path, new SqliteMemoryStore(path, new AesGcmMemoryProtector(key)));
    }
}
