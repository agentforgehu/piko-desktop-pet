using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Piko.Context.Events;
using Piko.Memory.Security;

namespace Piko.Memory;

public sealed class SqliteMemoryStore : IDisposable
{
    private const int MaximumRecords = 10_000;
    private readonly string _connectionString;
    private readonly IMemoryProtector _protector;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SqliteMemoryStore(string databasePath, IMemoryProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task<MemoryRecord> AddAsync(
        MemoryDraft draft,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateDraft(draft);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid();
        var expiresAt = draft.ExpiresAt ?? DefaultExpiry(draft.Kind, now);
        var summary = _protector.Protect(draft.Summary, $"{id:N}:summary");
        var payload = _protector.Protect(draft.Payload, $"{id:N}:payload");

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO memories
                (id, kind, created_at, updated_at, expires_at, sensitivity, source, summary_cipher, payload_cipher, provenance)
            VALUES
                ($id, $kind, $created, $updated, $expires, $sensitivity, $source, $summary, $payload, $provenance);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.Parameters.AddWithValue("$kind", (int)draft.Kind);
        command.Parameters.AddWithValue("$created", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$expires", expiresAt is null
            ? DBNull.Value
            : expiresAt.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sensitivity", (int)draft.Sensitivity);
        command.Parameters.AddWithValue("$source", draft.Source);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$provenance", (int)draft.Provenance);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var trim = connection.CreateCommand();
        trim.Transaction = (SqliteTransaction)transaction;
        trim.CommandText = """
            DELETE FROM memories
            WHERE id IN (
                SELECT id FROM memories
                ORDER BY updated_at DESC
                LIMIT -1 OFFSET $maximum
            );
            """;
        trim.Parameters.AddWithValue("$maximum", MaximumRecords);
        await trim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new MemoryRecord(
            id,
            draft.Kind,
            now,
            now,
            expiresAt,
            draft.Sensitivity,
            draft.Source,
            draft.Summary,
            draft.Payload,
            draft.Provenance);
    }

    public async Task<MemoryRecord?> GetAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, created_at, updated_at, expires_at, sensitivity, source, summary_cipher, payload_cipher, provenance
            FROM memories
            WHERE id = $id AND (expires_at IS NULL OR expires_at > $now);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadRecord(reader)
            : null;
    }

    public async Task<IReadOnlyList<MemoryRecord>> ListAsync(
        DateTimeOffset now,
        MemoryKind? kind = null,
        int limit = 100,
        CancellationToken cancellationToken = default,
        DataSensitivity? maximumSensitivity = null)
    {
        ThrowIfDisposed();
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, created_at, updated_at, expires_at, sensitivity, source, summary_cipher, payload_cipher, provenance
            FROM memories
            WHERE (expires_at IS NULL OR expires_at > $now)
              AND ($kind IS NULL OR kind = $kind)
              AND ($maximumSensitivity IS NULL OR sensitivity <= $maximumSensitivity)
            ORDER BY updated_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", kind is null ? DBNull.Value : (int)kind.Value);
        command.Parameters.AddWithValue("$maximumSensitivity", maximumSensitivity is null
            ? DBNull.Value
            : (int)maximumSensitivity.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var records = new List<MemoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
        string query,
        DateTimeOffset now,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(query);
        if (limit is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return Array.Empty<MemorySearchHit>();
        }

        // The store is intentionally small and local. Rank only a bounded recent candidate set,
        // and never expose medium/high sensitivity records to the model by default.
        var candidates = await ListAsync(
                now,
                limit: 500,
                cancellationToken: cancellationToken,
                maximumSensitivity: DataSensitivity.Low)
            .ConfigureAwait(false);
        return candidates
            .Where(record => record.Provenance != MemoryProvenance.Hypothetical)
            .Select(record => CreateActivation(query, queryTokens, record, now))
            .Where(hit => hit.Score >= 0.16)
            .OrderByDescending(hit => hit.Score)
            .ThenByDescending(hit => hit.Record.UpdatedAt)
            .Take(limit)
            .ToArray();
    }

    public async Task<int> PurgeExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await DeleteWhereAsync(
            "expires_at IS NOT NULL AND expires_at <= $value",
            now.ToString("O", CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);

    public async Task<int> DeleteKindAsync(
        MemoryKind kind,
        CancellationToken cancellationToken = default) =>
        await DeleteWhereAsync("kind = $value", (int)kind, cancellationToken).ConfigureAwait(false);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> UpdateAsync(
        Guid id,
        DateTimeOffset expectedUpdatedAt,
        MemoryDraft draft,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (expectedUpdatedAt == default)
        {
            throw new ArgumentException("An expected update timestamp is required.", nameof(expectedUpdatedAt));
        }
        ValidateDraft(draft);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var expiresAt = draft.ExpiresAt ?? DefaultExpiry(draft.Kind, now);
        var updatedAt = now > expectedUpdatedAt ? now : expectedUpdatedAt.AddTicks(1);
        var summary = _protector.Protect(draft.Summary, $"{id:N}:summary");
        var payload = _protector.Protect(draft.Payload, $"{id:N}:payload");
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE memories
            SET kind = $kind,
                updated_at = $updated,
                expires_at = $expires,
                sensitivity = $sensitivity,
                source = $source,
                summary_cipher = $summary,
                payload_cipher = $payload,
                provenance = $provenance
            WHERE id = $id AND updated_at = $expectedUpdated
              AND (expires_at IS NULL OR expires_at > $now);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.Parameters.AddWithValue("$expectedUpdated", expectedUpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", (int)draft.Kind);
        command.Parameters.AddWithValue("$updated", updatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$expires", expiresAt is null
            ? DBNull.Value
            : expiresAt.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sensitivity", (int)draft.Sensitivity);
        command.Parameters.AddWithValue("$source", draft.Source);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$provenance", (int)draft.Provenance);
        command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories;";
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var compact = connection.CreateCommand();
        compact.CommandText = "VACUUM;";
        await compact.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initializationGate.Dispose();
        _protector.Dispose();
    }

    private async Task<int> DeleteWhereAsync(
        string predicate,
        object value,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM memories WHERE {predicate};";
        command.Parameters.AddWithValue("$value", value);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS memories (
                    id TEXT PRIMARY KEY,
                    kind INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    expires_at TEXT NULL,
                    sensitivity INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    summary_cipher TEXT NOT NULL,
                    payload_cipher TEXT NOT NULL,
                    provenance INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_memories_expiry ON memories(expires_at);
                CREATE INDEX IF NOT EXISTS idx_memories_kind_updated ON memories(kind, updated_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var columns = connection.CreateCommand();
            columns.CommandText = "PRAGMA table_info(memories);";
            var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await columns.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    columnNames.Add(reader.GetString(1));
                }
            }
            if (!columnNames.Contains("provenance"))
            {
                await using var migrate = connection.CreateCommand();
                migrate.CommandText = "ALTER TABLE memories ADD COLUMN provenance INTEGER NOT NULL DEFAULT 0;";
                await migrate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private MemoryRecord ReadRecord(SqliteDataReader reader)
    {
        var id = Guid.ParseExact(reader.GetString(0), "N");
        return new MemoryRecord(
            id,
            (MemoryKind)reader.GetInt32(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            (DataSensitivity)reader.GetInt32(5),
            reader.GetString(6),
            _protector.Unprotect(reader.GetString(7), $"{id:N}:summary"),
            _protector.Unprotect(reader.GetString(8), $"{id:N}:payload"),
            (MemoryProvenance)reader.GetInt32(9));
    }

    private static DateTimeOffset? DefaultExpiry(MemoryKind kind, DateTimeOffset now) => kind switch
    {
        MemoryKind.Working => now.AddDays(1),
        MemoryKind.Episodic => now.AddDays(30),
        _ => null
    };

    private static MemorySearchHit CreateActivation(
        string query,
        HashSet<string> queryTokens,
        MemoryRecord record,
        DateTimeOffset now)
    {
        var candidateTokens = Tokenize(record.Summary);
        if (candidateTokens.Count == 0)
        {
            return new MemorySearchHit(record, 0, 0, 0, "no_lexical_overlap");
        }

        var overlap = queryTokens.Count(candidateTokens.Contains);
        var lexicalRelevance = overlap / Math.Sqrt(queryTokens.Count * candidateTokens.Count);
        var phraseBoost = query.Contains(record.Summary, StringComparison.OrdinalIgnoreCase) ||
                          record.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
            ? 0.25
            : 0;
        lexicalRelevance = Math.Min(1, lexicalRelevance + phraseBoost);

        var ageDays = Math.Max(0, (now - record.UpdatedAt).TotalDays);
        var halfLifeDays = record.Kind switch
        {
            MemoryKind.Working => 0.5,
            MemoryKind.Episodic => 14.0,
            _ => double.PositiveInfinity
        };
        var recencyWeight = double.IsPositiveInfinity(halfLifeDays)
            ? 1
            : Math.Pow(0.5, ageDays / halfLifeDays);
        var kindWeight = record.Kind switch
        {
            MemoryKind.Profile => 1.05,
            MemoryKind.Semantic or MemoryKind.Relationship => 1.03,
            MemoryKind.Working => 0.85,
            _ => 1.0
        };
        var provenanceWeight = record.Provenance switch
        {
            MemoryProvenance.UserConfirmed => 1.08,
            MemoryProvenance.ImportedKnowledge => 0.9,
            _ => 1.0
        };
        var activatedScore = Math.Min(1, lexicalRelevance * (0.65 + 0.35 * recencyWeight) * kindWeight * provenanceWeight);
        var reason = overlap == 0 && phraseBoost > 0
            ? "phrase_match"
            : record.Provenance == MemoryProvenance.UserConfirmed
                ? "lexical_match_user_confirmed"
                : record.Kind is MemoryKind.Working or MemoryKind.Episodic
                    ? "lexical_match_with_recency"
                    : "lexical_match_stable_fact";
        return new MemorySearchHit(record, activatedScore, lexicalRelevance, recencyWeight, reason);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var latin = new StringBuilder();
        var cjkRunes = new List<string>();

        void FlushLatin()
        {
            if (latin.Length > 0)
            {
                tokens.Add(Canonicalize(latin.ToString()));
                latin.Clear();
            }
        }

        void FlushCjk()
        {
            if (cjkRunes.Count == 1)
            {
                tokens.Add(cjkRunes[0]);
            }
            else
            {
                for (var index = 0; index + 1 < cjkRunes.Count; index++)
                {
                    tokens.Add(Canonicalize(cjkRunes[index] + cjkRunes[index + 1]));
                }
            }
            cjkRunes.Clear();
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.OtherLetter)
            {
                FlushLatin();
                cjkRunes.Add(rune.ToString());
            }
            else if (Rune.IsLetterOrDigit(rune))
            {
                FlushCjk();
                latin.Append(rune.ToString().ToLowerInvariant());
            }
            else
            {
                FlushLatin();
                FlushCjk();
            }
        }

        FlushLatin();
        FlushCjk();
        return tokens;
    }

    private static string Canonicalize(string token) => token.ToLowerInvariant() switch
    {
        "构建" or "编译" or "build" => "build",
        "失败" or "错误" or "failed" or "failure" or "error" => "failed",
        "成功" or "success" => "success",
        "测试" or "test" or "tests" => "test",
        _ => token.ToLowerInvariant()
    };

    private static void ValidateDraft(MemoryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!Enum.IsDefined(draft.Provenance) ||
            string.IsNullOrWhiteSpace(draft.Summary) || draft.Summary.Length > 8192 ||
            draft.Payload.Length > 65_536 ||
            string.IsNullOrWhiteSpace(draft.Source) || draft.Source.Length > 128)
        {
            throw new ArgumentException("Memory draft exceeds production bounds.", nameof(draft));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
