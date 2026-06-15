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
    private readonly Dictionary<IListSlot<TSource>, SingleAssignmentDisposable> _innerSubscriptions = new();
    private readonly CompositeDisposable _disposables = new();
    private readonly System.Reactive.Subjects.Subject<Action> _deferredActions = new();

    private IListOrchestrator<TSource, TInner, TResult>? _orchestrator;
    private int _subscriptionCount;
    private bool _isDisposed;
    private bool _hasTerminated;

    public ListOrchestration(
        IObservable<IChangeSet<TSource>> source,
        Func<IListOrchestratorContext<TSource, TInner>, IObserver<TResult>, IListOrchestrator<TSource, TInner, TResult>> orchestratorFactory,
        IObserver<TResult> downstream)
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        orchestratorFactory.ThrowArgumentNullExceptionIfNull(nameof(orchestratorFactory));
        downstream.ThrowArgumentNullExceptionIfNull(nameof(downstream));

        _downstream = downstream;
        _queue = new SharedDeliveryQueue(onDrainComplete: OnDrainComplete);

        try
        {
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
                        Fail(ex);
                    }
                }));

            _orchestrator = orchestratorFactory(this, _downstream);

            _subscriptionCount = 1;

            // CRITICAL: route the raw source THROUGH the SDQ before SlotAllocator sees it.
            // If SlotAllocator runs on the source-emit thread (outside the queue), it can
            // mutate slot.CurrentIndex / IsReleased while a previous drain on a different
            // thread is concurrently reading those fields from inner-observable handlers.
            // By queueing the source first, all slot allocation and index shifts happen on
            // the drain thread, in the same serialization domain as OnInner callbacks.
            var slottedSource = new SlotAllocator<TSource>(source.SynchronizeSafe(_queue)).Run();
            _disposables.Add(slottedSource.Subscribe(
                onNext: OnSourceNext,
                onError: OnSourceError,
                onCompleted: OnSourceCompleted));
        }
        catch
        {
            _disposables.Dispose();
            _queue.Dispose();
            _deferredActions.Dispose();
            throw;
        }
    }

    public void Track(IListSlot<TSource> slot, IObservable<TInner> observable)
    {
        slot.ThrowArgumentNullExceptionIfNull(nameof(slot));
        observable.ThrowArgumentNullExceptionIfNull(nameof(observable));

        if (_isDisposed) return;

        // Replace path: dispose existing and overwrite (no net subscription count change).
        if (_innerSubscriptions.TryGetValue(slot, out var existing))
        {
            existing.Dispose();
            _innerSubscriptions.Remove(slot);
        }
        else
        {
            Interlocked.Increment(ref _subscriptionCount);
        }

        // Register the container BEFORE calling Subscribe so that synchronously-completing
        // inner observables (Observable.Empty, Observable.Return, etc.) can find and dispose
        // their entry from the completion handler. Otherwise the entry would be written
        // after Subscribe returns, leaving a stale completed subscription that a later
        // Untrack would incorrectly decrement the count for a second time.
        var container = new SingleAssignmentDisposable();
        _innerSubscriptions[slot] = container;

        container.Disposable = observable.SynchronizeSafe(_queue).Subscribe(
            onNext: value => _orchestrator?.OnInner(value, slot, _downstream),
            onError: ex =>
            {
                TryReleaseOnTerminal(slot, container);
                Fail(ex);
            },
            onCompleted: () => TryReleaseOnTerminal(slot, container));
    }

    public void Untrack(IListSlot<TSource> slot)
    {
        slot.ThrowArgumentNullExceptionIfNull(nameof(slot));

        if (_innerSubscriptions.TryGetValue(slot, out var sub))
        {
            sub.Dispose();
            if (_innerSubscriptions.Remove(slot))
            {
                Interlocked.Decrement(ref _subscriptionCount);
            }
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

        // Stop incoming source notifications first so no new slots can be tracked.
        _disposables.Dispose();

        // Drain and terminate the queue. This blocks until any in-flight delivery completes,
        // guaranteeing that no further orchestrator callbacks (TrackSlot/UntrackSlot/etc.)
        // can mutate _innerSubscriptions concurrently with the iteration below.
        _queue.Dispose();

        // Orchestrator may own its own resources (buffer subscriptions, scheduler timers,
        // Subjects) - give it a chance to tear them down before we clear the inner table.
        (_orchestrator as IDisposable)?.Dispose();
        _orchestrator = null;

        _deferredActions.Dispose();

        foreach (var sub in _innerSubscriptions.Values) sub.Dispose();
        _innerSubscriptions.Clear();
    }

    private void TryReleaseOnTerminal(IListSlot<TSource> slot, SingleAssignmentDisposable container)
    {
        // Only release if THIS container is still the registered one. Defends against the case
        // where a replacement Track happened between when we subscribed and when the inner
        // completed, swapping out our container.
        if (_innerSubscriptions.TryGetValue(slot, out var current) && ReferenceEquals(current, container))
        {
            _innerSubscriptions.Remove(slot);
            Interlocked.Decrement(ref _subscriptionCount);
        }
    }

    private void OnSourceNext(IChangeSet<IListSlot<TSource>> changes)
    {
        if (_hasTerminated) return;
        try
        {
            _orchestrator?.OnSourceChangeSet(changes, this);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void OnSourceError(Exception ex)
    {
        Interlocked.Decrement(ref _subscriptionCount);
        Fail(ex);
    }

    private void OnSourceCompleted()
    {
        Interlocked.Decrement(ref _subscriptionCount);
    }

    private void OnDrainComplete()
    {
        if (_isDisposed || _hasTerminated) return;

        var isFinal = Volatile.Read(ref _subscriptionCount) == 0;
        try
        {
            _orchestrator?.OnDrainComplete(isFinal, _downstream);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }

        if (isFinal && !_hasTerminated)
        {
            _hasTerminated = true;
            _downstream.OnCompleted();
        }
    }

    private void Fail(Exception ex)
    {
        if (_hasTerminated) return;
        _hasTerminated = true;
        _downstream.OnError(ex);
    }
}
