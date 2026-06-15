// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DynamicData.List.Internal;

/// <summary>
/// Buffered variant of <see cref="AutoRefreshOrchestrator{TObject,TAny}"/>: refresh signals
/// from per-slot reevaluators are buffered over a time window and emitted as a coalesced
/// Refresh changeset when the window expires. Source pass-through changes are unaffected;
/// they emit on the drain cycle that delivered them.
/// </summary>
internal sealed class BufferedAutoRefreshOrchestrator<TObject, TAny> : IListOrchestrator<TObject, TAny, IChangeSet<TObject>>, IDisposable
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<TAny>> _reEvaluator;
    private readonly TimeSpan _buffer;
    private readonly IScheduler _scheduler;
    private readonly Subject<IListSlot<TObject>> _refreshSignals = new();
    private readonly List<Change<TObject>> _pendingSourceChanges = new();

    public BufferedAutoRefreshOrchestrator(
        Func<TObject, IObservable<TAny>> reEvaluator,
        TimeSpan buffer,
        IScheduler scheduler,
        IListOrchestratorContext<TObject, TAny> context,
        IObserver<IChangeSet<TObject>> emitter)
    {
        _reEvaluator = reEvaluator ?? throw new ArgumentNullException(nameof(reEvaluator));
        _buffer = buffer;
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        var batched = _refreshSignals
            .Buffer(_buffer, _scheduler)
            .Where(batch => batch.Count > 0);

        context.Serialize(batched).Subscribe(batch =>
        {
            var refreshes = new ChangeSet<TObject>(batch.Count);
            foreach (var slot in batch)
            {
                if (slot.IsReleased) continue;
                refreshes.Add(new Change<TObject>(ListChangeReason.Refresh, slot.Item, Optional<TObject>.None, slot.CurrentIndex));
            }

            if (refreshes.Count > 0)
            {
                emitter.OnNext(refreshes);
            }
        });
    }

    public void OnSourceChangeSet(IChangeSet<IListSlot<TObject>> changes, IListOrchestratorContext<TObject, TAny> context)
    {
        foreach (var change in changes)
        {
            _pendingSourceChanges.Add(change.Unwrap());

            switch (change.Reason)
            {
                case ListChangeReason.Add:
                    context.Track(change.Item.Current, _reEvaluator(change.Item.Current.Item));
                    break;
                case ListChangeReason.AddRange:
                    foreach (var slot in change.Range)
                    {
                        context.Track(slot, _reEvaluator(slot.Item));
                    }

                    break;
                case ListChangeReason.Replace:
                    if (change.Item.Previous.HasValue)
                    {
                        context.Untrack(change.Item.Previous.Value);
                    }

                    context.Track(change.Item.Current, _reEvaluator(change.Item.Current.Item));
                    break;
                case ListChangeReason.Remove:
                    context.Untrack(change.Item.Current);
                    break;
                case ListChangeReason.RemoveRange:
                case ListChangeReason.Clear:
                    foreach (var slot in change.Range)
                    {
                        context.Untrack(slot);
                    }

                    break;
            }
        }
    }

    public void OnInner(TAny value, IListSlot<TObject> slot, IObserver<IChangeSet<TObject>> emitter)
    {
        if (slot.IsReleased) return;
        _refreshSignals.OnNext(slot);
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<TObject>> emitter)
    {
        if (_pendingSourceChanges.Count == 0) return;

        var snapshot = new ChangeSet<TObject>(_pendingSourceChanges);
        _pendingSourceChanges.Clear();
        emitter.OnNext(snapshot);
    }

    public void Dispose() => _refreshSignals.Dispose();
}
