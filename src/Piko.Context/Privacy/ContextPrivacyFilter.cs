using Piko.Context.Events;

namespace Piko.Context.Privacy;

public sealed record PrivacyDecision(
    bool Allowed,
    ContextEvent? Event,
    string Reason,
    int RemovedFieldCount);

public sealed class ContextPrivacyFilter
{
    private readonly PrivacyProfile _profile;

    public ContextPrivacyFilter(PrivacyProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public PrivacyDecision Apply(ContextEvent contextEvent, PrivacyDestination destination)
    {
        ArgumentNullException.ThrowIfNull(contextEvent);
        var grant = _profile.GrantFor(contextEvent.Capability);
        if (grant == PermissionGrant.Denied)
        {
            return new PrivacyDecision(false, null, "capability_denied", contextEvent.Data.Count);
        }

        if (destination == PrivacyDestination.CloudAi &&
            _profile.GrantFor(ContextCapability.CloudAiProcessing) == PermissionGrant.Denied)
        {
            return new PrivacyDecision(false, null, "cloud_processing_denied", contextEvent.Data.Count);
        }

        var maximumSensitivity = destination switch
        {
            PrivacyDestination.CloudAi => _profile.MaximumCloudSensitivity,
            _ => _profile.MaximumLocalSensitivity
        };

        var retained = contextEvent.Data
            .Where(item => item.Value.Sensitivity <= maximumSensitivity)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var removed = contextEvent.Data.Count - retained.Count;

        var retention = destination == PrivacyDestination.LiveProcessing
            ? RetentionClass.None
            : grant == PermissionGrant.AllowSession
                ? RetentionClass.Session
                : contextEvent.Retention;

        return new PrivacyDecision(
            true,
            contextEvent.WithData(retained, retention),
            removed == 0 ? "allowed" : "sensitive_fields_removed",
            removed);
    }
}
