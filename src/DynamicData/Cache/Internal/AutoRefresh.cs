// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using DynamicData.Internal;

namespace DynamicData.Cache.Internal;

internal sealed class AutoRefresh<TObject, TKey, TAny>(IObservable<IChangeSet<TObject, TKey>> source, Func<TObject, TKey, IObservable<TAny>> reEvaluator, TimeSpan? buffer = null, IScheduler? scheduler = null)
    where TObject : notnull
    where TKey : notnull
{
    private readonly Func<TObject, TKey, IObservable<TAny>> _reEvaluator = reEvaluator ?? throw new ArgumentNullException(nameof(reEvaluator));

    private readonly IScheduler _scheduler = scheduler ?? GlobalConfig.DefaultScheduler;

    private readonly IObservable<IChangeSet<TObject, TKey>> _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<IChangeSet<TObject, TKey>> Run() => Observable.Create<IChangeSet<TObject, TKey>>(observer =>
    {
        var shared = _source.Publish();

        // Filters reevaluator emissions that fire synchronously during the initial Subscribe.
        // The triggering Add/Update already conveys the item's current state, so a paired
        // Refresh would be redundant. For an Add+Remove pair within a single source
        // changeset, it would also reference an item no longer in the cache.
        var refreshes = shared.MergeMany((t, k) =>
            Observable.Create<Change<TObject, TKey>>(innerObserver =>
            {
                var initialSubscribeInFlight = true;
                var subscription = _reEvaluator(t, k)
                    .Where(_ => !initialSubscribeInFlight)
                    .Select(_ => new Change<TObject, TKey>(ChangeReason.Refresh, k, t))
                    .Subscribe(innerObserver);
                initialSubscribeInFlight = false;
                return subscription;
            }));

        var refreshChangeSets = buffer is null
            ? refreshes.Select(static c => (IChangeSet<TObject, TKey>)new ChangeSet<TObject, TKey>(new[] { c }))
            : refreshes.Buffer(buffer.Value, _scheduler)
                       .Where(static list => list.Count > 0)
                       .Select(static items => (IChangeSet<TObject, TKey>)new ChangeSet<TObject, TKey>(items));

        var queue = new SharedDeliveryQueue();

        // Subscription order to `shared` is significant. Subject<T> notifies subscribers in
        // registration order, so subscribing the refresh branch first ensures each per-item
        // reevaluator subscription is wired up (and any synchronous side effects it performs
        // have executed) before the corresponding source change is delivered downstream.
        var publisher = refreshChangeSets.SynchronizeSafe(queue)
            .Merge(shared.SynchronizeSafe(queue))
            .SubscribeSafe(observer);

        return new CompositeDisposable(publisher, shared.Connect(), queue);
    });
}
