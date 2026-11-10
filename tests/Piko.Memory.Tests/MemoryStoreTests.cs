using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Piko.Cognition.Memory;
using Piko.Context.Events;
using Piko.Memory.Security;

namespace Piko.Memory.Tests;

public sealed class MemoryStoreTests
{
    [Fact]
    public async Task HostNeutralMemoryContractUsesEncryptedStoreAndPreservesConfidence()
    {
        var (root, _, storage) = CreateStore();
        try
        {
            ICognitiveMemoryStore memory = new CognitiveMemoryStoreAdapter(storage);
            var now = DateTimeOffset.UtcNow;
            var saved = await memory.AddAsync(new CognitiveMemoryDraft(
                MemoryCategory.Profile,
                "Prefers concise Chinese responses",
                "Use concise Chinese unless asked otherwise",
                MemorySensitivity.Low,
                "user.confirmed",
                Confidence: 0.93,
                EvidenceSource: MemoryEvidenceSource.UserConfirmed,
                FactKey: "response_style",
                FactValue: "concise_chinese"), now);
            var expired = await memory.AddAsync(new CognitiveMemoryDraft(
                MemoryCategory.Episodic,
                "Old temporary note",
                "retained only for user export",
                MemorySensitivity.Low,
                "test",
                Confidence: 0.2,
                ExpiresAt: now.AddDays(-1)), now);
            await memory.AddAsync(new CognitiveMemoryDraft(
                MemoryCategory.Profile,
                "private local-only note",
                "sensitive synthetic test value",
                MemorySensitivity.High,
                "user.confirmed",
                Confidence: 0.8,
                EvidenceSource: MemoryEvidenceSource.UserConfirmed), now);

            var hits = await memory.RecallAsync(new CognitiveMemoryRecallRequest(
                "Chinese responses", now, Category: MemoryCategory.Profile));
            var hit = Assert.Single(hits);
            var exported = await memory.ExportAsync(now);

            Assert.Equal(saved.Id, hit.Record.Id);
            Assert.Equal(0.93, hit.Record.Confidence);
            Assert.Equal(MemoryEvidenceSource.UserConfirmed, hit.Record.EvidenceSource);
            Assert.Equal("response_style", hit.Record.FactKey);
            Assert.Equal("concise_chinese", hit.Record.FactValue);
            Assert.Equal("Prefers concise Chinese responses", hit.Record.Summary);
            Assert.DoesNotContain(hits, item => item.Record.Id == expired.Id);
            Assert.Contains(exported, item => item.Id == expired.Id && item.Confidence == 0.2);
            Assert.Empty(await memory.RecallAsync(new CognitiveMemoryRecallRequest("private local-only note", now)));
            Assert.Single(await memory.RecallAsync(new CognitiveMemoryRecallRequest(
                "private local-only note", now, MaximumSensitivity: MemorySensitivity.High)));

            Assert.True(await memory.DeleteAsync(saved.Id));
            Assert.Null(await memory.GetAsync(saved.Id, now));
        }
        finally
        {
            storage.Dispose();
            Directory.Delete(root, true);
        }
    }

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
                    "test",
                    Provenance: MemoryProvenance.UserConfirmed,
                    FactKey: "response_language",
                    FactValue: "Chinese"),
                now);

            var loaded = await store.GetAsync(added.Id, now);
            Assert.Equal(added, loaded);
            var databaseBytes = await File.ReadAllBytesAsync(path);
            var databaseText = System.Text.Encoding.UTF8.GetString(databaseBytes);
            Assert.DoesNotContain("User fixed the build", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("No source code", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("response_language", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("Chinese", databaseText, StringComparison.Ordinal);
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
            var stableConfirmed = Assert.Single(hits, hit => hit.Record.Provenance == MemoryProvenance.UserConfirmed);
            var decayedEvent = Assert.Single(hits, hit => hit.Record.Kind == MemoryKind.Episodic);
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
    public async Task ExplicitReplacementRetainsHistoryAndOnlyActivatesCurrentVersion()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var old = await store.AddAsync(new MemoryDraft(MemoryKind.Profile,
                "用户偏好暗色主题", "dark theme", DataSensitivity.Low, "user.confirmed",
                Provenance: MemoryProvenance.UserConfirmed), now);
            var replacement = await store.ReplaceAsync(old.Id, old.UpdatedAt,
                new MemoryDraft(MemoryKind.Profile, "用户偏好浅色主题", "light theme", DataSensitivity.Low,
                    "user.confirmed", Provenance: MemoryProvenance.UserConfirmed), now.AddSeconds(1));

            Assert.NotNull(replacement);
            Assert.Equal("用户偏好浅色主题", replacement.Summary);
            var history = await store.ListAsync(now.AddSeconds(2));
            Assert.Equal(2, history.Count);
            Assert.True(Assert.Single(history, item => item.Id == old.Id).IsSuperseded);
            Assert.False(Assert.Single(history, item => item.Id == replacement.Id).IsSuperseded);
            var hits = await store.SearchAsync("用户偏好主题", now.AddSeconds(2));
            Assert.DoesNotContain(hits, hit => hit.Record.Id == old.Id);
            Assert.Contains(hits, hit => hit.Record.Id == replacement.Id);
            Assert.Null(await store.ReplaceAsync(old.Id, old.UpdatedAt,
                new MemoryDraft(MemoryKind.Profile, "another preference", "", DataSensitivity.Low,
                    "user.confirmed", Provenance: MemoryProvenance.UserConfirmed), now.AddSeconds(2)));
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RelatedMemorySuggestionsAreBoundedAndNeverLabelContradictions()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.AddAsync(new MemoryDraft(MemoryKind.Profile, "偏好使用简体中文回答", "", DataSensitivity.Low,
                "user.confirmed", Provenance: MemoryProvenance.UserConfirmed), now);
            await store.AddAsync(new MemoryDraft(MemoryKind.Profile, "偏好使用英文回答", "", DataSensitivity.Low,
                "user.confirmed", Provenance: MemoryProvenance.UserConfirmed), now.AddSeconds(1));
            await store.AddAsync(new MemoryDraft(MemoryKind.Profile, "偏好使用简体中文回答", "", DataSensitivity.Low,
                "example", Provenance: MemoryProvenance.Hypothetical), now.AddSeconds(2));
            await store.AddAsync(new MemoryDraft(MemoryKind.Episodic, "曾经使用简体中文", "", DataSensitivity.Low,
                "event", Provenance: MemoryProvenance.UserConfirmed), now.AddSeconds(3));

            var suggestions = await store.FindRelatedAsync(MemoryKind.Profile, "现在希望简体中文作答", now.AddSeconds(4));

            Assert.NotEmpty(suggestions);
            Assert.All(suggestions, hit => Assert.Equal("possible_related_user_confirmed", hit.ActivationReason));
            Assert.DoesNotContain(suggestions, hit => hit.Record.Provenance != MemoryProvenance.UserConfirmed);
            Assert.DoesNotContain(suggestions, hit => hit.Record.Kind != MemoryKind.Profile);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExplicitFactSlotsFindPossibleConflictsEvenWithoutLexicalOverlap()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(new MemoryDraft(
                MemoryKind.Profile, "unpaired fact metadata", "", DataSensitivity.Low, "user.confirmed",
                Provenance: MemoryProvenance.UserConfirmed, FactKey: "topic"), now));
            var chinese = await store.AddAsync(new MemoryDraft(MemoryKind.Profile,
                "希望回答简洁一些", "", DataSensitivity.Low, "user.confirmed",
                Provenance: MemoryProvenance.UserConfirmed,
                FactKey: "response_language", FactValue: "Chinese"), now);
            var english = await store.AddAsync(new MemoryDraft(MemoryKind.Profile,
                "Please keep answers concise", "", DataSensitivity.Low, "user.confirmed",
                Provenance: MemoryProvenance.UserConfirmed,
                FactKey: "response_language", FactValue: "English"), now.AddSeconds(1));
            await store.AddAsync(new MemoryDraft(MemoryKind.Profile,
                "Prefer a dark appearance", "", DataSensitivity.Low, "user.confirmed",
                Provenance: MemoryProvenance.UserConfirmed,
                FactKey: "theme", FactValue: "dark"), now.AddSeconds(2));

            var related = await store.FindRelatedAsync(MemoryKind.Profile, "简洁作答", now.AddSeconds(3),
                factKey: "response_language", factValue: "English");

            Assert.Equal(2, related.Count);
            Assert.Equal("same_fact_same_value", Assert.Single(related, hit => hit.Record.Id == english.Id).ActivationReason);
            Assert.Equal("same_fact_different_value", Assert.Single(related, hit => hit.Record.Id == chinese.Id).ActivationReason);
            Assert.DoesNotContain(related, hit => hit.Record.FactKey == "theme");
            Assert.DoesNotContain(related, hit => hit.ActivationReason.Contains("contradiction", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ValidityWindowsControlRecallButRemainVisibleForUserReview()
    {
        var (root, _, store) = CreateStore();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var active = await store.AddAsync(new MemoryDraft(MemoryKind.Profile, "偏好简体中文回答", "", DataSensitivity.Low,
                "user.confirmed", Provenance: MemoryProvenance.UserConfirmed,
                ValidFrom: now.AddDays(-1), ValidUntil: now.AddDays(1)), now);
            var future = await store.AddAsync(new MemoryDraft(MemoryKind.Profile, "偏好简体中文回答", "", DataSensitivity.Low,
                "user.confirmed", Provenance: MemoryProvenance.UserConfirmed,
                ValidFrom: now.AddHours(1), ValidUntil: now.AddDays(2)), now);
            var expired = await store.AddAsync(new MemoryDraft(MemoryKind.Profile, "偏好简体中文回答", "", DataSensitivity.Low,
                "user.confirmed", Provenance: MemoryProvenance.UserConfirmed,
                ValidFrom: now.AddDays(-2), ValidUntil: now.AddTicks(-1)), now);
            var scheduledEvent = await store.AddAsync(new MemoryDraft(MemoryKind.Episodic, "计划切换主题", "", DataSensitivity.Low,
                "user.confirmed", Provenance: MemoryProvenance.UserConfirmed,
                ValidFrom: now, ValidUntil: now.AddDays(60)), now);

            var history = await store.ListAsync(now);
            Assert.Equal(4, history.Count);
            Assert.Equal(now.AddHours(1), Assert.Single(history, item => item.Id == future.Id).ValidFrom);
            Assert.Equal(now.AddTicks(-1), Assert.Single(history, item => item.Id == expired.Id).ValidUntil);
            var hits = await store.SearchAsync("偏好简体中文回答", now);
            Assert.Equal(active.Id, Assert.Single(hits).Record.Id);
            var related = await store.FindRelatedAsync(MemoryKind.Profile, "偏好简体中文回答", now);
            Assert.Equal(active.Id, Assert.Single(related).Record.Id);
            Assert.True(scheduledEvent.ExpiresAt > scheduledEvent.ValidUntil);
            Assert.Contains(await store.ListAsync(now.AddDays(45)), item => item.Id == scheduledEvent.Id);
            Assert.Contains(await store.SearchAsync("计划切换主题", now.AddDays(45)), hit => hit.Record.Id == scheduledEvent.Id);
            Assert.DoesNotContain(await store.SearchAsync("计划切换主题", now.AddDays(61)), hit => hit.Record.Id == scheduledEvent.Id);
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
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
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
            Assert.Null(loaded.ValidFrom);
            Assert.Null(loaded.ValidUntil);
            Assert.Null(loaded.FactKey);
            Assert.Null(loaded.FactValue);
            Assert.Equal(0.5, loaded.Confidence);
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
