// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Orchestrator for the AutoRefresh shape: each source item gets a reevaluator subscription;
/// when the reevaluator fires, a Refresh change is emitted at the item's current index. Source
/// changes pass through unchanged. All changes within a single drain cycle are coalesced into
/// one downstream changeset.
/// </summary>
internal sealed class AutoRefreshOrchestrator<TObject, TAny> : IListOrchestrator<TObject, TAny, IChangeSet<TObject>>
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<TAny>> _reEvaluator;
    private readonly List<Change<TObject>> _pending = new();

    public AutoRefreshOrchestrator(Func<TObject, IObservable<TAny>> reEvaluator) =>
        _reEvaluator = reEvaluator ?? throw new ArgumentNullException(nameof(reEvaluator));

    public void OnSourceChangeSet(IChangeSet<IListSlot<TObject>> changes, IListOrchestratorContext<TObject, TAny> context)
    {
        foreach (var change in changes)
        {
            _pending.Add(change.Unwrap());

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

        _pending.Add(new Change<TObject>(ListChangeReason.Refresh, slot.Item, Optional<TObject>.None, slot.CurrentIndex));
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<TObject>> emitter)
    {
        if (_pending.Count == 0) return;

        var snapshot = new ChangeSet<TObject>(_pending);
        _pending.Clear();
        emitter.OnNext(snapshot);
    }
}
