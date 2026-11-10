using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
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

    [Fact]
    public async Task SearchRanksRelevantChineseAndEnglishSummariesAndExcludesSensitiveRecords()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.AddAsync(
                new MemoryDraft(MemoryKind.Episodic, "构建失败 Build failed", "{\"success\":false}", DataSensitivity.Low, "test"),
                now);
            await store.AddAsync(
                new MemoryDraft(MemoryKind.Episodic, "测试成功 Tests completed successfully", "{\"failed\":0}", DataSensitivity.Low, "test"),
                now.AddSeconds(1));
            await store.AddAsync(
                new MemoryDraft(MemoryKind.Profile, "构建失败 Build failed", "private", DataSensitivity.High, "test"),
                now.AddSeconds(2));

            var hits = await store.SearchAsync("上次构建失败了吗", now.AddSeconds(3));

            var hit = Assert.Single(hits);
            Assert.Contains("构建失败", hit.Record.Summary, StringComparison.Ordinal);
            Assert.Equal(DataSensitivity.Low, hit.Record.Sensitivity);
            Assert.InRange(hit.Score, 0.16, 1);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SearchReturnsNoResultsForUnrelatedQueryOrExpiredMemory()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.AddAsync(
                new MemoryDraft(MemoryKind.Episodic, "构建失败 Build failed", "{}", DataSensitivity.Low, "test", now),
                now.AddSeconds(-1));

            Assert.Empty(await store.SearchAsync("holiday recipe", now));
            Assert.Empty(await store.SearchAsync("构建失败", now));
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SearchUsesMemoryProvenanceAndRecencyInActivationAndExcludesHypotheticals()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.AddAsync(new MemoryDraft(
                MemoryKind.Episodic, "构建失败 Build failed", "{}", DataSensitivity.Low, "test"), now.AddDays(-20));
            await store.AddAsync(new MemoryDraft(
                MemoryKind.Profile, "构建失败 Build failed", "{}", DataSensitivity.Low, "test",
                Provenance: MemoryProvenance.UserConfirmed), now.AddDays(-20));
            await store.AddAsync(new MemoryDraft(
                MemoryKind.Profile, "构建失败 Build failed", "{}", DataSensitivity.Low, "test",
                Provenance: MemoryProvenance.Hypothetical), now);

            var hits = await store.SearchAsync("构建失败", now);

            Assert.Equal(2, hits.Count);
            Assert.DoesNotContain(hits, hit => hit.Record.Provenance == MemoryProvenance.Hypothetical);
            var stableConfirmed = Assert.Single(hits.Where(hit => hit.Record.Provenance == MemoryProvenance.UserConfirmed));
            var decayedEvent = Assert.Single(hits.Where(hit => hit.Record.Kind == MemoryKind.Episodic));
            Assert.True(stableConfirmed.Score > decayedEvent.Score);
            Assert.Equal("lexical_match_user_confirmed", stableConfirmed.ActivationReason);
            Assert.True(decayedEvent.RecencyWeight < 0.5);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExistingMemoryDatabaseMigratesAndMarksOldRowsAsObservedEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "PikoMemoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "memory.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using (var protector = new AesGcmMemoryProtector(key))
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE memories (
                    id TEXT PRIMARY KEY, kind INTEGER NOT NULL, created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL, expires_at TEXT NULL, sensitivity INTEGER NOT NULL,
                    source TEXT NOT NULL, summary_cipher TEXT NOT NULL, payload_cipher TEXT NOT NULL);
                INSERT INTO memories VALUES ($id, $kind, $created, $updated, NULL, $sensitivity,
                    $source, $summary, $payload);
                """;
            command.Parameters.AddWithValue("$id", id.ToString("N"));
            command.Parameters.AddWithValue("$kind", (int)MemoryKind.Profile);
            command.Parameters.AddWithValue("$created", now.ToString("O"));
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.Parameters.AddWithValue("$sensitivity", (int)DataSensitivity.Low);
            command.Parameters.AddWithValue("$source", "legacy");
            command.Parameters.AddWithValue("$summary", protector.Protect("legacy preference", $"{id:N}:summary"));
            command.Parameters.AddWithValue("$payload", protector.Protect("legacy payload", $"{id:N}:payload"));
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteMemoryStore(path, new AesGcmMemoryProtector(key));
        try
        {
            var loaded = await store.GetAsync(id, now);
            Assert.NotNull(loaded);
            Assert.Equal(MemoryProvenance.ObservedEvent, loaded.Provenance);
            Assert.Equal("legacy preference", loaded.Summary);
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
