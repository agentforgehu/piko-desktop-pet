using Piko.Context.Events;

namespace Piko.Context.Situations;

public sealed class SituationEngine
{
    private readonly SituationEngineOptions _options;
    private PresenceState _presence = PresenceState.Unknown;
    private ApplicationCategory _application = ApplicationCategory.Unknown;
    private bool _isFullscreen;
    private bool _isActivelyTyping;
    private bool _buildRunning;
    private int _consecutiveBuildFailures;
    private int _diagnosticErrors;
    private int _failedTests;
    private DateTimeOffset? _returnedUntil;
    private DateTimeOffset? _lastObservedAt;

    public SituationEngine(SituationEngineOptions? options = null)
    {
        _options = options ?? new SituationEngineOptions();
        Current = SituationState.Unknown(DateTimeOffset.UnixEpoch);
    }

    public SituationState Current { get; private set; }

    public SituationState Observe(ContextEvent contextEvent)
    {
        ArgumentNullException.ThrowIfNull(contextEvent);
        if (_lastObservedAt is { } last &&
            contextEvent.Timestamp < last - _options.MaximumOutOfOrderAge)
        {
            return Current;
        }

        var previousPresence = _presence;
        Apply(contextEvent);
        if (previousPresence is PresenceState.Idle or PresenceState.Locked &&
            _presence == PresenceState.Active)
        {
            _returnedUntil = contextEvent.Timestamp + _options.ReturnedDuration;
        }

        if (_presence is PresenceState.Idle or PresenceState.Locked)
        {
            _returnedUntil = null;
        }

        _lastObservedAt = _lastObservedAt is null || contextEvent.Timestamp > _lastObservedAt
            ? contextEvent.Timestamp
            : _lastObservedAt;

        return Evaluate(contextEvent.Timestamp);
    }

    public SituationState Evaluate(DateTimeOffset now)
    {
        var (kind, confidence, evidence) = Resolve(now);
        var startedAt = Current.Kind == kind ? Current.StartedAt : now;
        Current = new SituationState(
            kind,
            startedAt,
            now,
            confidence,
            evidence,
            _consecutiveBuildFailures,
            _isActivelyTyping,
            _isFullscreen);
        return Current;
    }

    private void Apply(ContextEvent contextEvent)
    {
        switch (contextEvent.Type)
        {
            case ContextEventTypes.PresenceChanged when contextEvent.TryGetString("state", out var presence):
                _presence = ParseEnum(presence, PresenceState.Unknown);
                break;
            case ContextEventTypes.ForegroundApplicationChanged when contextEvent.TryGetString("category", out var category):
                _application = ParseEnum(category, ApplicationCategory.Unknown);
                break;
            case ContextEventTypes.FullscreenChanged when contextEvent.TryGetBoolean("active", out var fullscreen):
                _isFullscreen = fullscreen;
                break;
            case ContextEventTypes.InputIntensityChanged when contextEvent.TryGetString("level", out var level):
                _isActivelyTyping = level.Equals("high", StringComparison.OrdinalIgnoreCase);
                break;
            case ContextEventTypes.BuildStarted:
                _buildRunning = true;
                break;
            case ContextEventTypes.BuildCompleted when contextEvent.TryGetBoolean("success", out var buildSucceeded):
                _buildRunning = false;
                _consecutiveBuildFailures = buildSucceeded ? 0 : _consecutiveBuildFailures + 1;
                break;
            case ContextEventTypes.DiagnosticsChanged when contextEvent.TryGetInt32("errors", out var errors):
                _diagnosticErrors = Math.Max(0, errors);
                break;
            case ContextEventTypes.TestsCompleted when contextEvent.TryGetInt32("failed", out var failedTests):
                _failedTests = Math.Max(0, failedTests);
                break;
        }
    }

    private (SituationKind Kind, double Confidence, IReadOnlyList<string> Evidence) Resolve(DateTimeOffset now)
    {
        if (_presence is PresenceState.Idle or PresenceState.Locked)
        {
            return (SituationKind.Away, 1, new[] { $"presence:{_presence.ToString().ToLowerInvariant()}" });
        }

        if (_returnedUntil is { } returnedUntil && now < returnedUntil)
        {
            return (SituationKind.Returned, 1, new[] { "presence:return" });
        }

        if (_application == ApplicationCategory.Communication && _isFullscreen)
        {
            return (SituationKind.Meeting, 0.8, new[] { "app:communication", "display:fullscreen" });
        }

        if (_application == ApplicationCategory.Gaming ||
            _application == ApplicationCategory.Media && _isFullscreen)
        {
            return _application == ApplicationCategory.Gaming
                ? (SituationKind.Gaming, 0.9, new[] { "app:gaming" })
                : (SituationKind.WatchingMedia, 0.85, new[] { "app:media", "display:fullscreen" });
        }

        if (_buildRunning)
        {
            return (SituationKind.Building, 1, new[] { "development:build-running" });
        }

        if (_application == ApplicationCategory.Development)
        {
            if (_consecutiveBuildFailures > 0 || _diagnosticErrors > 0 || _failedTests > 0)
            {
                var evidence = new List<string> { "app:development" };
                if (_consecutiveBuildFailures > 0)
                {
                    evidence.Add($"build:failed:{_consecutiveBuildFailures}");
                }
                if (_diagnosticErrors > 0)
                {
                    evidence.Add($"diagnostics:errors:{_diagnosticErrors}");
                }
                if (_failedTests > 0)
                {
                    evidence.Add($"tests:failed:{_failedTests}");
                }

                return (SituationKind.CodingBlocked, _consecutiveBuildFailures > 0 ? 0.95 : 0.78, evidence);
            }

            return (SituationKind.Coding, 0.85, new[] { "app:development" });
        }

        if (_application == ApplicationCategory.Office)
        {
            return (SituationKind.FocusedWork, 0.75, new[] { "app:office" });
        }

        if (_application == ApplicationCategory.Media)
        {
            return (SituationKind.WatchingMedia, 0.7, new[] { "app:media" });
        }

        if (_presence == PresenceState.Active)
        {
            return (SituationKind.Active, 0.7, new[] { "presence:active" });
        }

        return (SituationKind.Unknown, 0, Array.Empty<string>());
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
}
