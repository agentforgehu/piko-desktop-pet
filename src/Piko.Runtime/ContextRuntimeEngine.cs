using Piko.Context.Events;
using Piko.Context.Interventions;
using Piko.Context.Privacy;
using Piko.Context.Situations;

namespace Piko.Runtime;

public sealed record ContextRuntimeUpdate(
    bool Accepted,
    string Reason,
    SituationState Situation,
    InterventionDecision Intervention,
    ContextDispatchReceipt? DispatchReceipt);

public sealed class ContextRuntimeEngine : IDisposable
{
    private readonly ContextPrivacyFilter _privacy;
    private readonly ContextEventBus _bus;
    private readonly SituationEngine _situations;
    private readonly InterventionPolicy _interventions;
    private readonly IDisposable _situationSubscription;
    private readonly SemaphoreSlim _processingGate = new(1, 1);
    private SituationKind _previousSituation = SituationKind.Unknown;

    public ContextRuntimeEngine(
        PrivacyProfile? profile = null,
        ContextEventBus? eventBus = null,
        SituationEngine? situations = null,
        InterventionPolicy? interventions = null)
    {
        _privacy = new ContextPrivacyFilter(profile ?? PrivacyProfile.LocalFirst());
        _bus = eventBus ?? new ContextEventBus();
        _situations = situations ?? new SituationEngine();
        _interventions = interventions ?? new InterventionPolicy();
        _situationSubscription = _bus.Subscribe((contextEvent, _) =>
        {
            _previousSituation = _situations.Current.Kind;
            _situations.Observe(contextEvent);
            return ValueTask.CompletedTask;
        });
    }

    public SituationState CurrentSituation => _situations.Current;

    public async ValueTask<ContextRuntimeUpdate> ProcessAsync(
        ContextEvent contextEvent,
        bool quietHours = false,
        CancellationToken cancellationToken = default)
    {
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var privacyDecision = _privacy.Apply(contextEvent, PrivacyDestination.LiveProcessing);
        if (!privacyDecision.Allowed || privacyDecision.Event is null)
        {
            return new ContextRuntimeUpdate(
                false,
                privacyDecision.Reason,
                _situations.Current,
                InterventionDecision.None("event_rejected"),
                null);
        }

        var receipt = await _bus.PublishAsync(privacyDecision.Event, cancellationToken)
            .ConfigureAwait(false);
        var intervention = _interventions.Decide(new InterventionContext(
            _situations.Current,
            _previousSituation,
            contextEvent.Timestamp,
            QuietHours: quietHours));
        return new ContextRuntimeUpdate(
            true,
            privacyDecision.Reason,
            _situations.Current,
            intervention,
            receipt);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    public void Dispose()
    {
        _situationSubscription.Dispose();
        _bus.Dispose();
        _processingGate.Dispose();
    }
}
