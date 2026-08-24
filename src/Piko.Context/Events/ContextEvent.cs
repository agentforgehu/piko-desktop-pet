using System.Collections.ObjectModel;
using System.Globalization;

namespace Piko.Context.Events;

public sealed record ContextEvent
{
    public const int CurrentSchemaVersion = 1;

    private ContextEvent(
        Guid eventId,
        int schemaVersion,
        string type,
        string source,
        DateTimeOffset timestamp,
        string sessionId,
        string? correlationId,
        double confidence,
        ContextCapability capability,
        DataSensitivity sensitivity,
        RetentionClass retention,
        IReadOnlyDictionary<string, ContextDataValue> data)
    {
        EventId = eventId;
        SchemaVersion = schemaVersion;
        Type = type;
        Source = source;
        Timestamp = timestamp;
        SessionId = sessionId;
        CorrelationId = correlationId;
        Confidence = confidence;
        Capability = capability;
        Sensitivity = sensitivity;
        Retention = retention;
        Data = data;
    }

    public Guid EventId { get; }
    public int SchemaVersion { get; }
    public string Type { get; }
    public string Source { get; }
    public DateTimeOffset Timestamp { get; }
    public string SessionId { get; }
    public string? CorrelationId { get; }
    public double Confidence { get; }
    public ContextCapability Capability { get; }
    public DataSensitivity Sensitivity { get; }
    public RetentionClass Retention { get; }
    public IReadOnlyDictionary<string, ContextDataValue> Data { get; }

    public static ContextEvent Create(
        string type,
        string source,
        DateTimeOffset timestamp,
        string sessionId,
        ContextCapability capability,
        DataSensitivity sensitivity = DataSensitivity.Low,
        RetentionClass retention = RetentionClass.Session,
        double confidence = 1,
        IReadOnlyDictionary<string, ContextDataValue>? data = null,
        string? correlationId = null,
        Guid? eventId = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        ValidateIdentifier(type, nameof(type));
        ValidateIdentifier(source, nameof(source));
        ValidateIdentifier(sessionId, nameof(sessionId));
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        if (confidence is < 0 or > 1 || double.IsNaN(confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        var values = data is null
            ? new Dictionary<string, ContextDataValue>(StringComparer.Ordinal)
            : new Dictionary<string, ContextDataValue>(data, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            ValidateIdentifier(key, "data key");
            ArgumentNullException.ThrowIfNull(value);
            if (value.Value.Length > 4096)
            {
                throw new ArgumentException($"Context field '{key}' exceeds 4096 characters.", nameof(data));
            }
        }

        return new ContextEvent(
            eventId ?? Guid.NewGuid(),
            schemaVersion,
            type,
            source,
            timestamp,
            sessionId,
            correlationId,
            confidence,
            capability,
            sensitivity,
            retention,
            new ReadOnlyDictionary<string, ContextDataValue>(values));
    }

    public ContextEvent WithData(
        IReadOnlyDictionary<string, ContextDataValue> data,
        RetentionClass? retention = null) =>
        Create(
            Type,
            Source,
            Timestamp,
            SessionId,
            Capability,
            Sensitivity,
            retention ?? Retention,
            Confidence,
            data,
            CorrelationId,
            EventId,
            SchemaVersion);

    public bool TryGetString(string key, out string value)
    {
        if (Data.TryGetValue(key, out var field))
        {
            value = field.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public bool TryGetBoolean(string key, out bool value)
    {
        value = default;
        return TryGetString(key, out var text) && bool.TryParse(text, out value);
    }

    public bool TryGetInt32(string key, out int value)
    {
        value = default;
        return TryGetString(key, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new ArgumentException("Context identifiers must contain 1 to 128 characters.", parameterName);
        }
    }
}
