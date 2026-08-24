using System.Globalization;
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
        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO memories
                (id, kind, created_at, updated_at, expires_at, sensitivity, source, summary_cipher, payload_cipher)
            VALUES
                ($id, $kind, $created, $updated, $expires, $sensitivity, $source, $summary, $payload);
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
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var trim = connection.CreateCommand();
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
            draft.Payload);
    }

    public async Task<MemoryRecord?> GetAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, created_at, updated_at, expires_at, sensitivity, source, summary_cipher, payload_cipher
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
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, created_at, updated_at, expires_at, sensitivity, source, summary_cipher, payload_cipher
            FROM memories
            WHERE (expires_at IS NULL OR expires_at > $now)
              AND ($kind IS NULL OR kind = $kind)
            ORDER BY updated_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", kind is null ? DBNull.Value : (int)kind.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var records = new List<MemoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
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

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories;";
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var compact = connection.CreateCommand();
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
        var command = connection.CreateCommand();
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
            var command = connection.CreateCommand();
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
                    payload_cipher TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_memories_expiry ON memories(expires_at);
                CREATE INDEX IF NOT EXISTS idx_memories_kind_updated ON memories(kind, updated_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        var command = connection.CreateCommand();
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
            _protector.Unprotect(reader.GetString(8), $"{id:N}:payload"));
    }

    private static DateTimeOffset? DefaultExpiry(MemoryKind kind, DateTimeOffset now) => kind switch
    {
        MemoryKind.Working => now.AddDays(1),
        MemoryKind.Episodic => now.AddDays(30),
        _ => null
    };

    private static void ValidateDraft(MemoryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Summary) || draft.Summary.Length > 8192 ||
            draft.Payload.Length > 65_536 ||
            string.IsNullOrWhiteSpace(draft.Source) || draft.Source.Length > 128)
        {
            throw new ArgumentException("Memory draft exceeds production bounds.", nameof(draft));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
