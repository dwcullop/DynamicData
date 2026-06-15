// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

internal sealed class MergeManyItems<TObject, TDestination>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<TDestination>> observableSelector)
    where TObject : notnull
{
    private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Func<TObject, IObservable<TDestination>> _observableSelector = observableSelector ?? throw new ArgumentNullException(nameof(observableSelector));

    public IObservable<ItemWithValue<TObject, TDestination>> Run() =>
        _source.Orchestrate<TObject, TDestination, ItemWithValue<TObject, TDestination>>(
            onSourceChangeSet: (changes, context) =>
            {
                foreach (var change in changes)
                {
                    switch (change.Reason)
                    {
                        case ListChangeReason.Add:
                            context.Track(change.Item.Current, _observableSelector(change.Item.Current.Item));
                            break;
                        case ListChangeReason.AddRange:
                            foreach (var slot in change.Range)
                            {
                                context.Track(slot, _observableSelector(slot.Item));
                            }

                            break;
                        case ListChangeReason.Replace:
                            if (change.Item.Previous.HasValue)
                            {
                                context.Untrack(change.Item.Previous.Value);
                            }

                            context.Track(change.Item.Current, _observableSelector(change.Item.Current.Item));
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
            },
            onInner: (value, slot, emitter) =>
            {
                // Guard against zombie emissions: an inner observable may have a queued
                // emission that drains after the slot has already been untracked (e.g.,
                // source removed the item but the inner was mid-flight). Skip in that case
                // so we don't emit a value for an item the caller has already seen removed.
                if (slot.IsReleased) return;
                emitter.OnNext(new ItemWithValue<TObject, TDestination>(slot.Item, value));
            });
}
