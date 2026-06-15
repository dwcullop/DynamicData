// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;

namespace DynamicData.List.Internal;

internal sealed class TrueFor<T, TValue>(IObservable<IChangeSet<T>> source, Func<T, IObservable<TValue>> observableSelector, Func<IEnumerable<ItemWithLatest<T, TValue>>, bool> collectionMatcher)
    where T : notnull
    where TValue : notnull
{
    private readonly IObservable<IChangeSet<T>> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Func<T, IObservable<TValue>> _observableSelector = observableSelector ?? throw new ArgumentNullException(nameof(observableSelector));
    private readonly Func<IEnumerable<ItemWithLatest<T, TValue>>, bool> _collectionMatcher = collectionMatcher ?? throw new ArgumentNullException(nameof(collectionMatcher));

    public IObservable<bool> Run()
    {
        var perSlot = new Dictionary<IListSlot<T>, ItemWithLatest<T, TValue>>();

        return _source.Orchestrate<T, TValue, bool>(
            onSourceChangeSet: (changes, context) =>
            {
                foreach (var change in changes)
                {
                    switch (change.Reason)
                    {
                        case ListChangeReason.Add:
                            TrackSlot(change.Item.Current, context);
                            break;
                        case ListChangeReason.AddRange:
                            foreach (var slot in change.Range) TrackSlot(slot, context);
                            break;
                        case ListChangeReason.Replace:
                            if (change.Item.Previous.HasValue) UntrackSlot(change.Item.Previous.Value, context);
                            TrackSlot(change.Item.Current, context);
                            break;
                        case ListChangeReason.Remove:
                            UntrackSlot(change.Item.Current, context);
                            break;
                        case ListChangeReason.RemoveRange:
                        case ListChangeReason.Clear:
                            foreach (var slot in change.Range) UntrackSlot(slot, context);
                            break;
                    }
                }
            },
            onInner: (value, slot, _) =>
            {
                if (perSlot.TryGetValue(slot, out var box)) box.LatestValue = value;
            },
            onDrainComplete: (_, emitter) =>
            {
                emitter.OnNext(_collectionMatcher(perSlot.Values));
            })
            .DistinctUntilChanged();

        void TrackSlot(IListSlot<T> slot, IListOrchestratorContext<T, TValue> ctx)
        {
            perSlot[slot] = new ItemWithLatest<T, TValue>(slot.Item);
            ctx.Track(slot, _observableSelector(slot.Item));
        }

        void UntrackSlot(IListSlot<T> slot, IListOrchestratorContext<T, TValue> ctx)
        {
            ctx.Untrack(slot);
            perSlot.Remove(slot);
        }
    }
}
