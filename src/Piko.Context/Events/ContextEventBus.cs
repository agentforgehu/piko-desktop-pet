namespace Piko.Context.Events;

public delegate ValueTask ContextEventHandler(ContextEvent contextEvent, CancellationToken cancellationToken);

public sealed record ContextDispatchFailure(long SubscriptionId, Exception Exception);

public sealed record ContextDispatchReceipt(
    Guid EventId,
    int HandlerCount,
    int SuccessfulHandlers,
    IReadOnlyList<ContextDispatchFailure> Failures);

public sealed class ContextEventBus : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly SortedDictionary<long, ContextEventHandler> _handlers = new();
    private long _nextSubscriptionId;
    private bool _disposed;

    public IDisposable Subscribe(ContextEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = ++_nextSubscriptionId;
            _handlers.Add(id, handler);
            return new Subscription(this, id);
        }
    }

    public async ValueTask<ContextDispatchReceipt> PublishAsync(
        ContextEvent contextEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contextEvent);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            KeyValuePair<long, ContextEventHandler>[] handlers;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                handlers = _handlers.ToArray();
            }

            var failures = new List<ContextDispatchFailure>();
            foreach (var (id, handler) in handlers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await handler(contextEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(new ContextDispatchFailure(id, exception));
                }
            }

            return new ContextDispatchReceipt(
                contextEvent.EventId,
                handlers.Length,
                handlers.Length - failures.Count,
                failures.AsReadOnly());
        }
        finally
        {
            _dispatchGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _handlers.Clear();
        }

        _dispatchGate.Dispose();
    }

    private void Unsubscribe(long id)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _handlers.Remove(id);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private ContextEventBus? _owner;
        private readonly long _id;

        public Subscription(ContextEventBus owner, long id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Unsubscribe(_id);
    }
}
