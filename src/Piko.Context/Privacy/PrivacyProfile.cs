using System.Collections.ObjectModel;
using Piko.Context.Events;

namespace Piko.Context.Privacy;

public sealed record PrivacyProfile
{
    private PrivacyProfile(
        IReadOnlyDictionary<ContextCapability, PermissionGrant> grants,
        DataSensitivity maximumLocalSensitivity,
        DataSensitivity maximumCloudSensitivity)
    {
        Grants = grants;
        MaximumLocalSensitivity = maximumLocalSensitivity;
        MaximumCloudSensitivity = maximumCloudSensitivity;
    }

    public IReadOnlyDictionary<ContextCapability, PermissionGrant> Grants { get; }
    public DataSensitivity MaximumLocalSensitivity { get; }
    public DataSensitivity MaximumCloudSensitivity { get; }

    public static PrivacyProfile LocalFirst()
    {
        var grants = Enum.GetValues<ContextCapability>()
            .ToDictionary(capability => capability, _ => PermissionGrant.Denied);

        foreach (var capability in new[]
                 {
                     ContextCapability.Presence,
                     ContextCapability.ForegroundApplicationCategory,
                     ContextCapability.FullscreenState,
                     ContextCapability.FileActivity,
                     ContextCapability.SystemHealth
                 })
        {
            grants[capability] = PermissionGrant.AllowAlways;
        }

        return Create(grants, DataSensitivity.Medium, DataSensitivity.Public);
    }

    public static PrivacyProfile Create(
        IReadOnlyDictionary<ContextCapability, PermissionGrant> grants,
        DataSensitivity maximumLocalSensitivity = DataSensitivity.Medium,
        DataSensitivity maximumCloudSensitivity = DataSensitivity.Low)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var complete = Enum.GetValues<ContextCapability>()
            .ToDictionary(
                capability => capability,
                capability => grants.GetValueOrDefault(capability, PermissionGrant.Denied));

        return new PrivacyProfile(
            new ReadOnlyDictionary<ContextCapability, PermissionGrant>(complete),
            maximumLocalSensitivity,
            maximumCloudSensitivity);
    }

    public PermissionGrant GrantFor(ContextCapability capability) =>
        Grants.GetValueOrDefault(capability, PermissionGrant.Denied);

    public PrivacyProfile WithGrant(ContextCapability capability, PermissionGrant grant)
    {
        var grants = Grants.ToDictionary(item => item.Key, item => item.Value);
        grants[capability] = grant;
        return Create(grants, MaximumLocalSensitivity, MaximumCloudSensitivity);
    }
}
