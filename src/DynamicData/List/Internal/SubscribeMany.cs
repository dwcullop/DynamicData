// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DynamicData.List.Internal;

internal sealed class SubscribeMany<T>(IObservable<IChangeSet<T>> source, Func<T, IDisposable> subscriptionFactory)
    where T : notnull
{
    private readonly IObservable<IChangeSet<T>> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Func<T, IDisposable> _subscriptionFactory = subscriptionFactory ?? throw new ArgumentNullException(nameof(subscriptionFactory));

    public IObservable<IChangeSet<T>> Run() => Observable.Create<IChangeSet<T>>(observer =>
    {
        // Per-slot disposable tracking. Slots are stable across list mutations except removal,
        // so duplicate source items correctly get distinct subscriptions (the legacy Transform+
        // DisposeMany implementation matched by value, mishandling duplicates).
        var subscriptions = new Dictionary<IListSlot<T>, IDisposable>();

        var orchestrated = _source.Orchestrate<T, Unit, IChangeSet<T>>(
            onSourceChangeSet: (changes, _) =>
            {
                foreach (var change in changes)
                {
                    switch (change.Reason)
                    {
                        case ListChangeReason.Add:
                            Add(change.Item.Current);
                            break;
                        case ListChangeReason.AddRange:
                            foreach (var slot in change.Range) Add(slot);
                            break;
                        case ListChangeReason.Replace:
                            if (change.Item.Previous.HasValue) RemoveSub(change.Item.Previous.Value);
                            Add(change.Item.Current);
                            break;
                        case ListChangeReason.Remove:
                            RemoveSub(change.Item.Current);
                            break;
                        case ListChangeReason.RemoveRange:
                        case ListChangeReason.Clear:
                            foreach (var slot in change.Range) RemoveSub(slot);
                            break;
                    }
                }
            },
            onInner: (_, _, _) => { });

        var publisher = orchestrated.Subscribe(_ => { }, observer.OnError);
        var sourcePub = _source.Subscribe(observer.OnNext, observer.OnError, observer.OnCompleted);

        return new CompositeDisposable(
            publisher,
            sourcePub,
            Disposable.Create(() =>
            {
                foreach (var sub in subscriptions.Values) sub.Dispose();
                subscriptions.Clear();
            }));

        void Add(IListSlot<T> slot)
        {
            if (subscriptions.TryGetValue(slot, out var existing)) existing.Dispose();
            subscriptions[slot] = _subscriptionFactory(slot.Item);
        }

        void RemoveSub(IListSlot<T> slot)
        {
            if (subscriptions.TryGetValue(slot, out var existing))
            {
                existing.Dispose();
                subscriptions.Remove(slot);
            }
        }
    });
}
