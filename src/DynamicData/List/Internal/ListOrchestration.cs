// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Disposables;
using System.Reactive.Linq;

using DynamicData.Internal;

namespace DynamicData.List.Internal;

/// <summary>
/// Per-subscription driver for list orchestrators. Owns the <see cref="SharedDeliveryQueue"/>,
/// the per-slot inner subscriptions, the subscription counter, and disposal lifecycle. Built
/// once per downstream subscription via the public Orchestrate extension methods.
/// </summary>
internal sealed class ListOrchestration<TSource, TInner, TResult>
    : IListOrchestratorContext<TSource, TInner>, IDisposable
    where TSource : notnull
{
    private readonly IObserver<TResult> _downstream;
    private readonly SharedDeliveryQueue _queue;
    private readonly Dictionary<IListSlot<TSource>, IDisposable> _innerSubscriptions = new();
    private readonly CompositeDisposable _disposables = new();
    private readonly System.Reactive.Subjects.Subject<Action> _deferredActions = new();

    private IListOrchestrator<TSource, TInner, TResult>? _orchestrator;
    private int _subscriptionCount;
    private bool _isDisposed;

    public ListOrchestration(
        IObservable<IChangeSet<TSource>> source,
        Func<IListOrchestratorContext<TSource, TInner>, IObserver<TResult>, IListOrchestrator<TSource, TInner, TResult>> orchestratorFactory,
        IObserver<TResult> downstream)
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        orchestratorFactory.ThrowArgumentNullExceptionIfNull(nameof(orchestratorFactory));
        downstream.ThrowArgumentNullExceptionIfNull(nameof(downstream));

        _downstream = downstream;

        try
        {
            _queue = new SharedDeliveryQueue(onDrainComplete: OnDrainComplete);

            // Subscribe the deferred-action stream EARLY through the queue. This gives it a
            // low index in the SharedDeliveryQueue's sub-queue list. Because the SDQ drains
            // higher-index sub-queues first (LIFO), deferred actions fire AFTER any inner
            // subscriptions that are added later via Track.
            _disposables.Add(_deferredActions.SynchronizeSafe(_queue).Subscribe(
                onNext: action =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        _downstream.OnError(ex);
                    }
                }));

            _orchestrator = orchestratorFactory(this, _downstream);

            _subscriptionCount = 1;
            var slottedSource = new SlotAllocator<TSource>(source).Run();
            _disposables.Add(slottedSource.SynchronizeSafe(_queue).Subscribe(
                onNext: OnSourceNext,
                onError: OnSourceError,
                onCompleted: OnSourceCompleted));
        }
        catch
        {
            _disposables.Dispose();
            _queue?.Dispose();
            throw;
        }
    }

    public void Track(IListSlot<TSource> slot, IObservable<TInner> observable)
    {
        slot.ThrowArgumentNullExceptionIfNull(nameof(slot));
        observable.ThrowArgumentNullExceptionIfNull(nameof(observable));

        if (_isDisposed) return;

        if (_innerSubscriptions.TryGetValue(slot, out var existing))
        {
            existing.Dispose();
            _innerSubscriptions.Remove(slot);
            Interlocked.Decrement(ref _subscriptionCount);
        }

        Interlocked.Increment(ref _subscriptionCount);
        var sub = observable.SynchronizeSafe(_queue).Subscribe(
            onNext: value => _orchestrator?.OnInner(value, slot, _downstream),
            onError: ex =>
            {
                _innerSubscriptions.Remove(slot);
                Interlocked.Decrement(ref _subscriptionCount);
                _downstream.OnError(ex);
            },
            onCompleted: () =>
            {
                _innerSubscriptions.Remove(slot);
                Interlocked.Decrement(ref _subscriptionCount);
            });

        _innerSubscriptions[slot] = sub;
    }

    public void Untrack(IListSlot<TSource> slot)
    {
        slot.ThrowArgumentNullExceptionIfNull(nameof(slot));

        if (_innerSubscriptions.TryGetValue(slot, out var sub))
        {
            sub.Dispose();
            _innerSubscriptions.Remove(slot);
            Interlocked.Decrement(ref _subscriptionCount);
        }
    }

    public IObservable<T> Serialize<T>(IObservable<T> observable)
    {
        observable.ThrowArgumentNullExceptionIfNull(nameof(observable));
        return observable.SynchronizeSafe(_queue);
    }

    public void DeferAction(Action action)
    {
        action.ThrowArgumentNullExceptionIfNull(nameof(action));
        if (_isDisposed) return;
        _deferredActions.OnNext(action);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var sub in _innerSubscriptions.Values) sub.Dispose();
        _innerSubscriptions.Clear();
        _disposables.Dispose();
        _deferredActions.Dispose();
        _queue.Dispose();
    }

    private void OnSourceNext(IChangeSet<IListSlot<TSource>> changes)
    {
        try
        {
            _orchestrator?.OnSourceChangeSet(changes, this);
        }
        catch (Exception ex)
        {
            _downstream.OnError(ex);
        }
    }

    private void OnSourceError(Exception ex)
    {
        Interlocked.Decrement(ref _subscriptionCount);
        _downstream.OnError(ex);
    }

    private void OnSourceCompleted()
    {
        Interlocked.Decrement(ref _subscriptionCount);
    }

    private void OnDrainComplete()
    {
        if (_isDisposed) return;

        var isFinal = Volatile.Read(ref _subscriptionCount) == 0;
        try
        {
            _orchestrator?.OnDrainComplete(isFinal, _downstream);
        }
        catch (Exception ex)
        {
            _downstream.OnError(ex);
            return;
        }

        if (isFinal)
        {
            _downstream.OnCompleted();
        }
    }
}
