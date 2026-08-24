using Piko.Context.Events;

namespace Piko.Runtime.Ipc;

public sealed record RuntimeContextDataField(
    string Value,
    string Sensitivity = "low");

public sealed record RuntimeContextEventEnvelope(
    int SchemaVersion,
    string Type,
    string Source,
    DateTimeOffset Timestamp,
    string SessionId,
    string Capability,
    IReadOnlyDictionary<string, RuntimeContextDataField>? Data = null,
    string Sensitivity = "low",
    string Retention = "session",
    double Confidence = 1,
    string? CorrelationId = null)
{
    public const int CurrentSchemaVersion = 1;

    public bool TryCreateContextEvent(out ContextEvent? contextEvent, out string error)
    {
        contextEvent = null;
        error = string.Empty;
        if (SchemaVersion != CurrentSchemaVersion)
        {
            error = "unsupported_context_schema";
            return false;
        }

        if (!Enum.TryParse<ContextCapability>(Capability, true, out var capability) ||
            !Enum.TryParse<DataSensitivity>(Sensitivity, true, out var sensitivity) ||
            !Enum.TryParse<RetentionClass>(Retention, true, out var retention))
        {
            error = "invalid_context_enum";
            return false;
        }

        if (Data?.Count > 64)
        {
            error = "too_many_context_fields";
            return false;
        }

        try
        {
            var data = (Data ?? new Dictionary<string, RuntimeContextDataField>())
                .ToDictionary(
                    item => item.Key,
                    item => new ContextDataValue(
                        item.Value.Value,
                        Enum.TryParse<DataSensitivity>(item.Value.Sensitivity, true, out var fieldSensitivity)
                            ? fieldSensitivity
                            : throw new ArgumentException("Invalid field sensitivity.")),
                    StringComparer.Ordinal);
            contextEvent = ContextEvent.Create(
                Type,
                Source,
                Timestamp,
                SessionId,
                capability,
                sensitivity,
                retention,
                Confidence,
                data,
                CorrelationId);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            error = "invalid_context_event";
            return false;
        }
    }
}

public sealed record RuntimeContextPublishResult(
    bool Accepted,
    string Reason,
    string Situation,
    string Intervention);
