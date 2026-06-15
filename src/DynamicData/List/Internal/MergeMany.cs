// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

internal sealed class MergeMany<T, TDestination>(IObservable<IChangeSet<T>> source, Func<T, IObservable<TDestination>> observableSelector)
    where T : notnull
{
    private readonly Func<T, IObservable<TDestination>> _observableSelector = observableSelector ?? throw new ArgumentNullException(nameof(observableSelector));

    private readonly IObservable<IChangeSet<T>> _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<TDestination> Run() =>
        _source.Orchestrate<T, TDestination, TDestination>(
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
            onInner: (value, _, emitter) => emitter.OnNext(value));
}
